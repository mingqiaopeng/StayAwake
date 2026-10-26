// ============================================================================
//  StayAwake - 开机自启 + 配置持久化
// ============================================================================

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace StayAwake
{
    /// <summary>开机自启注册项的三种状态。</summary>
    internal enum AutoStartState
    {
        /// <summary>没有注册项。</summary>
        None = 0,

        /// <summary>已注册，且指向当前这个 exe。</summary>
        Current = 1,

        /// <summary>
        /// 有注册项，但指向别的路径 —— exe 被移动/改名后的典型遗留。
        /// 早先只要「值非空」就算已注册，于是勾号打着、开机却跑的是旧路径。
        /// </summary>
        Foreign = 2
    }

    internal struct AutoStartInfo
    {
        internal AutoStartState State;
        internal string Command;

        /// <summary>注册项存在即为「已开启」，Foreign 也要打勾 —— 否则勾号会骗人。</summary>
        internal bool Registered { get { return State != AutoStartState.None; } }
    }

    /// <summary>
    /// 开机自启管理：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    /// 用 HKCU 而非 HKLM，避免需要管理员权限。
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "StayAwake";

        private static readonly object Sync = new object();
        private static AutoStartInfo _cached;
        private static bool _cacheValid;
        private static long _cacheStampMs;

        /// <summary>
        /// 缓存有效期。
        ///
        /// 原先只在 Set() 成功后才失效，于是「任务管理器 → 启动应用」里
        /// 改掉启动项、或者 exe 被移动改名之后，托盘里的勾号会一直显示旧状态。
        /// 每秒重读一次注册表太频繁，10 秒一次足够跟上 UI 的节奏，开销可忽略。
        /// </summary>
        private const long CacheTtlMs = 10000;

        /// <summary>最近一次 Set 失败的原因，成功时为 null。</summary>
        internal static string LastError { get; private set; }

        /// <summary>直接读注册表。读不出来时按「未注册」处理，仅供自检使用。</summary>
        internal static AutoStartInfo Query()
        {
            AutoStartInfo info;
            if (TryQuery(out info)) return info;

            info = new AutoStartInfo();
            info.State = AutoStartState.None;
            info.Command = null;
            return info;
        }

        /// <summary>
        /// 真正读注册表。返回 false 表示「读不到」（权限 / 键被锁 / 瞬时失败），
        /// 与「确实没注册」的 None 区分开 —— 后者是有效结果，前者不该污染缓存。
        /// </summary>
        private static bool TryQuery(out AutoStartInfo info)
        {
            info = new AutoStartInfo();
            info.State = AutoStartState.None;
            info.Command = null;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return true;          // 键不在 = 未注册

                    object val = key.GetValue(ValueName);
                    if (val == null) return true;         // 值不在 = 未注册

                    string cmd = val.ToString();
                    if (cmd.Length == 0) return true;

                    info.Command = cmd;
                    info.State = PathMatchesSelf(cmd) ? AutoStartState.Current
                                                     : AutoStartState.Foreign;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("读取开机自启注册项失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 带缓存的查询。UI 每秒刷新状态，不该每秒都去碰注册表；
        /// 但也不能一读到就永远不再重读，否则外部改动永远反映不到界面上。
        /// </summary>
        internal static AutoStartInfo Cached()
        {
            lock (Sync)
            {
                long now = Native.MonotonicMs;
                if (!_cacheValid || now - _cacheStampMs >= CacheTtlMs)
                {
                    AutoStartInfo fresh;
                    if (TryQuery(out fresh))
                    {
                        _cached = fresh;
                        _cacheValid = true;
                        _cacheStampMs = now;
                    }
                    // 读失败：保留上一次已知状态，不把「读不到」误报成「未注册」，
                    // 否则勾号会每 10 秒闪一次。
                }
                return _cached;
            }
        }

        internal static void InvalidateCache()
        {
            lock (Sync) { _cacheValid = false; _cacheStampMs = 0; }
        }

        /// <summary>是否已注册（不校验路径，仅用于「有没有这一项」）。</summary>
        internal static bool IsEnabled()
        {
            return Cached().Registered;
        }

        internal static bool Set(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        LastError = "无法打开 HKCU\\...\\Run（键不存在或被访问策略拒绝）";
                        Log.Warn("设置开机自启失败: " + LastError);
                        return false;
                    }

                    if (enable)
                    {
                        // 带引号兼容含空格路径。默认启动即托盘常驻 + 自动守护，
                        // 与直接双击 exe 行为一致。
                        key.SetValue(ValueName, ExeCommand);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                LastError = null;
                InvalidateCache();
                Log.Info("开机自启已" + (enable ? "开启: " + ExeCommand : "关闭"));
                return true;
            }
            catch (Exception ex)
            {
                // 保留真实原因：权限不足 / 键被锁 / 组策略限制 等
                LastError = ex.GetType().Name + ": " + ex.Message;
                Log.Warn("设置开机自启失败: " + LastError);
                return false;
            }
        }

        /// <summary>写入注册表的标准命令行（带引号）。</summary>
        internal static string ExeCommand
        {
            get { return "\"" + ExePath + "\""; }
        }

        /// <summary>当前 exe 完整路径</summary>
        internal static string ExePath
        {
            get
            {
                try
                {
                    // using：Process 持有内核句柄。ExePath 会被自启校验、
                    // 菜单开关、自检反复读取，不释放就是慢性句柄泄漏。
                    using (Process proc = Process.GetCurrentProcess())
                    {
                        string p = proc.MainModule.FileName;
                        if (!string.IsNullOrEmpty(p)) return p;
                    }

                    string asm = System.Reflection.Assembly.GetEntryAssembly().Location;
                    if (!string.IsNullOrEmpty(asm)) return asm;

                    return AppDomain.CurrentDomain.BaseDirectory + "StayAwake.exe";
                }
                catch (Exception ex)
                {
                    Log.Warn("获取 exe 路径失败: " + ex.Message);
                    return "StayAwake.exe";
                }
            }
        }

        /// <summary>
        /// 从注册表命令行里抽出可执行文件路径再和自身比对。
        /// 命令行后面可能跟参数，所以要一直找到第一个「不以引号包裹的路径段」为止。
        /// </summary>
        private static bool PathMatchesSelf(string command)
        {
            try
            {
                string exe = ExtractExeFromCommand(command);
                if (string.IsNullOrEmpty(exe)) return false;

                return string.Equals(Normalize(exe), Normalize(ExePath),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractExeFromCommand(string command)
        {
            string s = command.Trim();
            if (s.Length == 0) return null;

            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end < 0) return null;
                return s.Substring(1, end - 1);
            }

            // 没加引号：取到第一个命令行分隔符为止
            int stop = s.IndexOf(' ');
            return stop < 0 ? s : s.Substring(0, stop);
        }

        /// <summary>规范化路径用于比较：统一分隔符、去掉结尾斜杠。</summary>
        private static string Normalize(string path)
        {
            string p = path.Replace('/', '\\').TrimEnd('\\');
            try { return Path.GetFullPath(p); }
            catch { return p; }
        }
    }

    /// <summary>
    /// 配置持久化：%APPDATA%\StayAwake\settings.ini
    ///
    /// 写入走「临时文件 + 原子替换」。直接 File.WriteAllText 在断电/被杀时
    /// 会留下截断文件，而读取又是静默吞异常 —— 用户配置全丢却毫无提示。
    /// </summary>
    internal static class Settings
    {
        /// <summary>当前生效的配置。引擎和两个界面共享同一个实例。</summary>
        internal static readonly KeepAwakeOptions Current = new KeepAwakeOptions();

        internal static string FilePath
        {
            get
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(dir, "StayAwake", "settings.ini");
            }
        }

        internal static void Load()
        {
            KeepAwakeOptions o = new KeepAwakeOptions();
            string path = FilePath;

            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;

                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim();
                        bool on = v == "1";

                        switch (k)
                        {
                            case "Sleep":       o.PreventSleep = on; break;
                            case "Display":     o.PreventDisplay = on; break;
                            case "Saver":       o.DisableScreenSaver = on; break;
                            case "SimMouse":    o.SimMouse = on; break;
                            case "SimKey":      o.SimKeyboard = on; break;
                            case "OnlyIdle":    o.OnlyWhenIdle = on; break;
                            case "Interval":
                                int n;
                                if (int.TryParse(v, NumberStyles.Integer,
                                                 CultureInfo.InvariantCulture, out n))
                                    o.IntervalSeconds = KeepAwakeOptions.ClampInterval(n);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 配置读不出来不是致命错误：用默认值继续，但一定要留下痕迹。
                Log.Warn("读取配置失败（改用默认值）: " + ex.Message);
                o = new KeepAwakeOptions();
            }

            Current.CopyFrom(o);
            Log.Info("配置载入 | " + Current.Describe());
        }

        /// <summary>把给定配置写盘，并同步到 Current。</summary>
        internal static void Save(KeepAwakeOptions options)
        {
            if (options == null) return;

            Current.CopyFrom(options);

            string path = FilePath;
            string tmp = path + ".tmp";
            string bak = path + ".bak";

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder(256);
                sb.Append("# StayAwake 配置，手工编辑无效").Append(Environment.NewLine);
                sb.Append("Sleep=").Append(b(Current.PreventSleep)).Append(Environment.NewLine);
                sb.Append("Display=").Append(b(Current.PreventDisplay)).Append(Environment.NewLine);
                sb.Append("Saver=").Append(b(Current.DisableScreenSaver)).Append(Environment.NewLine);
                sb.Append("SimMouse=").Append(b(Current.SimMouse)).Append(Environment.NewLine);
                sb.Append("SimKey=").Append(b(Current.SimKeyboard)).Append(Environment.NewLine);
                sb.Append("OnlyIdle=").Append(b(Current.OnlyWhenIdle)).Append(Environment.NewLine);
                sb.Append("Interval=").Append(
                    KeepAwakeOptions.ClampInterval(Current.IntervalSeconds).ToString(
                        CultureInfo.InvariantCulture));

                // 先写临时文件，成功后再原子替换 —— 中途崩溃最坏只是丢一次改动，
                // 不会把已有配置写成半截。
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, bak);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }

                Log.Debug("配置已保存 | " + Current.Describe());
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败: " + ex.Message, ex);
            }
        }

        private static string b(bool v)
        {
            return v ? "1" : "0";
        }
    }
}