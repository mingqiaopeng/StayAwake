// ============================================================================
//  StayAwake - 程序入口
//
//  启动模式:
//    (默认)        托盘常驻，启动即守护，无窗口
//    --gui         图形界面
//    --silent      后台静默，无界面无托盘（计划任务用）
//    --selftest    自检，打印诊断信息后退出
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StayAwake
{
    internal static class Program
    {
        private const string MutexName = @"Local\StayAwake.SingleInstance";

        [STAThread]
        private static int Main(string[] args)
        {
            // 必须在 EnableVisualStyles 之前、在创建任何控件之前。
            // 原先放在 MainForm.BuildUi 里、且排在 ClientSize 赋值之后：
            // 切 DPI 感知会把已经算好的尺寸重新解释一遍，而托盘模式压根没调过，
            // 150% 屏幕上托盘图标就是糊的 —— 正好废掉了整套超采样管线。
            TrySetPerMonitorDpi();

            if (HasFlag(args, "--selftest"))
            {
                Settings.Load();
                return SelfTest.Run();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasFlag(args, "--verbose"))
                Log.SetVerbose(true);

            Settings.Load();

            bool createdNew;
            using (Mutex mtx = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "StayAwake 已在运行，请查看系统托盘。",
                        "StayAwake", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                try
                {
                    Log.Info("启动 | " + Environment.CommandLine);

                    // 后台静默，无界面无托盘（计划任务用）
                    if (HasFlag(args, "--silent"))
                        return RunSilent(args);

                    // 图形界面（需要显式指定）
                    if (HasFlag(args, "--gui"))
                    {
                        Application.Run(new MainForm());
                        return 0;
                    }

                    // 默认：托盘常驻 + 自动开始守护，不显示任何界面
                    // --no-autostart 可用于「仅驻留但先不守护」
                    bool auto = !HasFlag(args, "--no-autostart");
                    Application.Run(new TrayController(auto));
                    return 0;
                }
                catch (Exception ex)
                {
                    // 兜底顶层异常处理：记日志 + 告知用户，而不是静默消失
                    Log.Error("未捕获的致命异常", ex);
                    try
                    {
                        MessageBox.Show("StayAwake 发生错误，已记录到日志：\n\n"
                            + ex.Message + "\n\n日志：" + Log.FilePath,
                            "StayAwake", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch { }
                    return 1;
                }
                finally
                {
                    Log.Info("退出");
                    mtx.ReleaseMutex();
                }
            }
        }


        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // --------------------------------------------------------------------
        //  静默模式
        // --------------------------------------------------------------------
        private static int RunSilent(string[] args)
        {
            KeepAwakeOptions opt = new KeepAwakeOptions();
            opt.CopyFrom(Settings.Current);

            foreach (KeyValuePair<string, string> kv in ParseSilentArgs(args))
            {
                int n;
                switch (kv.Key)
                {
                    case "interval":
                        if (int.TryParse(kv.Value, out n))
                        {
                            if (n >= 5 && n <= 3600)
                                opt.IntervalSeconds = n;
                            else
                                Log.Warn("--interval " + n + " 超出 5~3600，已忽略");
                        }
                        else
                        {
                            Log.Warn("--interval 参数无法解析: " + kv.Value);
                        }
                        break;
                    case "no-mouse":  opt.SimMouse = false; break;
                    case "no-key":    opt.SimKeyboard = false; break;
                    case "always":    opt.OnlyWhenIdle = false; break;
                }
            }

            using (KeepAwakeEngine engine = new KeepAwakeEngine())
            using (ManualResetEvent stop = new ManualResetEvent(false))
            {
                ConsoleCancelEventHandler cancel =
                    delegate(object s, ConsoleCancelEventArgs e)
                    {
                        e.Cancel = true;
                        stop.Set();
                    };

                bool hooked = false;
                try
                {
                    Console.CancelKeyPress += cancel;
                    hooked = true;
                }
                catch (Exception ex)
                {
                    Log.Warn("无法挂接 Ctrl+C 处理器: " + ex.Message);
                }

                try
                {
                    engine.Start(opt);
                    SilentLoop(engine, stop);
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Error("静默模式异常退出", ex);
                    return 2;
                }
                finally
                {
                    if (hooked)
                    {
                        try { Console.CancelKeyPress -= cancel; }
                        catch { }
                    }
                    engine.Stop();
                }
            }
        }

        /// <summary>解析 --interval N / --no-mouse / --no-key / --always。</summary>
        private static List<KeyValuePair<string, string>> ParseSilentArgs(string[] args)
        {
            List<KeyValuePair<string, string>> result =
                new List<KeyValuePair<string, string>>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] == null ? "" : args[i];

                if (Is(a, "--interval", "/interval"))
                {
                    // 原先匹配到 --interval 却不跳过它的值，
                    // 那个值下一轮循环会当普通参数再走一遍。这里明确前进一格。
                    if (i + 1 < args.Length)
                    {
                        result.Add(new KeyValuePair<string, string>("interval", args[i + 1]));
                        i++;
                    }
                    else
                    {
                        Log.Warn("--interval 缺少参数值");
                    }
                }
                else if (Is(a, "--no-mouse")) result.Add(Kv("no-mouse"));
                else if (Is(a, "--no-key"))   result.Add(Kv("no-key"));
                else if (Is(a, "--always"))   result.Add(Kv("always"));
            }

            return result;
        }

        private static KeyValuePair<string, string> Kv(string key)
        {
            return new KeyValuePair<string, string>(key, null);
        }

        private static bool Is(string value, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(value, names[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void SilentLoop(KeepAwakeEngine engine, ManualResetEvent stop)
        {
            // 引擎自己管节奏，这里只负责可中断地等。
            // 早先在循环里额外 sleep(interval) 是多余的 —— Tick() 内部已经
            // 按单调时钟判过时间，外层再睡一轮只是让节奏漂移。
            while (!stop.WaitOne(1000))
            {
                engine.Tick();
            }
        }

        // --------------------------------------------------------------------
        //  DPI
        // --------------------------------------------------------------------
        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        /// <summary>
        /// 尽力设为 Per-Monitor DPI 感知。
        /// app.manifest 里已声明 PerMonitorV2，所以正常构建下这里基本是兜底路径
        /// （再调一次会返回 E_ACCESSDENIED，属预期，忽略即可）。
        /// </summary>
        private static void TrySetPerMonitorDpi()
        {
            try { SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE); return; }
            catch (Exception) { }

            try { SetProcessDPIAware(); }
            catch (Exception) { }
        }

        // ========================================================================
        //  自检
        // ========================================================================
        /// <summary>
        /// --selftest：把 README 里那张靠手工记录的「实测数据」表变成可复现脚本。
        /// winexe 没有控制台，需要 AttachConsole 到父进程才能输出。
        /// </summary>
        internal static class SelfTest
        {
            private static int _fail;
            private static StringBuilder _sb;

            internal static int Run()
            {
                TextWriter w = OpenOutput();
                _sb = new StringBuilder(4096);

                try
                {
                    Head("StayAwake 自检  " +
                         DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    Line("", new string('=', 60));

                    Head("环境");
                    Line("操作系统版本", Environment.OSVersion.VersionString);
                    Line("64 位进程", Environment.Is64BitProcess.ToString());
                    Line("会话 Id", CurrentSessionId());                    Line("exe 路径", AutoStart.ExePath);

                    Head("嵌入资源");
                    foreach (string res in new[] { "SA_ICON_GREY.png", "SA_ICON_LIGHT.png",
                                                  "SA_ICON_RED.png", "SA_ICON_NAVY.png" })
                    {
                        CheckResource(res);
                    }

                    Head("图标渲染");
                    for (int i = 0; i <= 2; i++)
                    {
                        CheckTrayIcon((TrayState)i);
                    }

                    Head("屏幕保护程序");
                    CheckScreenSaver();

                    Head("电源请求");
                    CheckExecutionState();

                    Head("空闲检测");
                    CheckIdle();

                    Head("输入模拟");
                    CheckSendInput();

                    Head("开机自启注册表");
                    CheckAutoStart();

                    Head("配置文件");
                    CheckSettings();

                    Head("其它");
                    Line("日志文件", Log.FilePath);

                    Line("", new string('=', 60));
                    Line("", _fail == 0
                        ? "全部通过"
                        : _fail + " 项异常（详见上方 [FAIL] 行）");

                    Emit(w);
                    return _fail == 0 ? 0 : 1;
                }
                catch (Exception ex)
                {
                    _fail++;
                    Fail("自检", "自身抛出异常: " + ex.Message);
                    try { Emit(w); } catch { }
                    return 2;
                }
            }

            // --------------------------------------------------------------
            //  报告累积与输出
            // --------------------------------------------------------------
            private static void Head(string title)
            {
                _sb.AppendLine();
                _sb.AppendLine("[" + title + "]");
            }

            private static void Line(string k, string v)
            {
                _sb.AppendLine("  " + k.PadRight(14) + " " + v);
            }

            private static void Ok(string k, string v)
            {
                _sb.AppendLine("  [ OK ] " + k.PadRight(14) + " " + v);
            }

            private static void Fail(string k, string v)
            {
                _fail++;
                _sb.AppendLine("  [FAIL] " + k.PadRight(14) + " " + v);
            }

            /// <summary>
            /// 输出报告。
            /// 优先写「启动时就已存在的 stdout」—— 被重定向到管道/文件时
            /// 靠它才能拿到输出；只有在没有可写句柄时才去 AttachConsole。
            /// （反过来做会让 AllocConsole 把继承来的管道句柄顶掉，输出全丢。）
            /// </summary>
            private static void Emit(TextWriter w)
            {
                string report = _sb.ToString();

                if (w != null)
                {
                    try { w.Write(report); w.Flush(); return; }
                    catch (Exception) { }
                }

                TextWriter fallback = OpenConsole();
                if (fallback != null)
                {
                    try { fallback.Write(report); fallback.Flush(); return; }
                    catch (Exception) { }
                }

                // 两条路都走不通：落盘，保证结果不丢
                string file = WriteReportFile(report);
                try
                {
                    MessageBox.Show("自检无法输出到控制台，报告已写入：\n\n" + file,
                        "StayAwake 自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception) { }
            }

            private static string WriteReportFile(string report)
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StayAwake");
                string file = Path.Combine(dir,
                    "selftest-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                try
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(file, report, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    file += " (写入失败: " + ex.Message + ")";
                }
                return file;
            }

            // --------------------------------------------------------------
            //  各项检查
            // --------------------------------------------------------------
            private static void CheckResource(string name)
            {
                try
                {
                    using (System.Drawing.Image img = TrayIconFactory.LoadResource(name))
                        Ok(name, img.Width + "x" + img.Height + " " + img.PixelFormat);
                }
                catch (Exception ex)
                {
                    Fail(name, ex.Message);
                }
            }

            /// <summary>当前会话 Id。Process 持有内核句柄，用完必须释放。</summary>
            private static string CurrentSessionId()
            {
                try
                {
                    using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess())
                        return p.SessionId.ToString();
                }
                catch (Exception ex) { return "未知 (" + ex.GetType().Name + ")"; }
            }

            private static void CheckTrayIcon(TrayState state)            {
                try
                {
                    using (System.Drawing.Icon ic = TrayIconFactory.CreateTrayIcon(state))
                    {
                        if (ic == null)
                        {
                            Fail("状态 " + (int)state, "返回了 null");
                            return;
                        }

                        System.Drawing.Size sz;
                        // ToBitmap() 每次都新建一个 Bitmap（自带 GDI+ 原生缓冲），
                        // 不释放的话自检一次就漏五个。
                        using (System.Drawing.Bitmap bmp = ic.ToBitmap())
                            sz = bmp.Size;

                        Ok("状态 " + (int)state,
                           ic.Width + "px  ICO=" + ic.Height + "px  位图=" + sz.Width + "x" + sz.Height);
                    }
                }
                catch (Exception ex)
                {
                    Fail("状态 " + (int)state, ex.Message);
                }
            }

            private static void CheckScreenSaver()
            {
                bool enabled;
                if (!Native.TryGetScreenSaverEnabled(out enabled))
                {
                    Fail("读取", "SPI_GETSCREENSAVEACTIVE(0x0010) 调用失败");
                    return;
                }
                Ok("当前状态", enabled ? "启用" : "禁用");

                // 写回原值，验证 SET/GET 成对可用（不会改变你的设置）
                if (Native.SetScreenSaverEnabled(enabled))
                {
                    bool back;
                    if (Native.TryGetScreenSaverEnabled(out back) && back == enabled)
                        Ok("写回原值", "成功");
                    else
                        Fail("写回原值", "写入后读回不一致");
                }
                else
                {
                    Fail("写回原值", "SPI_SETSCREENSAVEACTIVE(0x0011) 返回 false");
                }
            }

            private static void CheckExecutionState()
            {
                try
                {
                    uint r = Native.SetThreadExecutionState(Native.ES_CONTINUOUS
                        | Native.ES_SYSTEM_REQUIRED | Native.ES_DISPLAY_REQUIRED);
                    if (r == 0)
                        Fail("提交请求", "返回 0，请求未生效");
                    else
                        Ok("提交请求", "返回 0x" + r.ToString("X") + "（非 0 即成功）");

                    Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
                    Ok("释放请求", "完成");
                }
                catch (Exception ex)
                {
                    Fail("调用", ex.Message);
                }
            }

            private static void CheckIdle()
            {
                long idle = KeepAwakeEngine.GetIdleMilliseconds();
                if (idle < 0)
                    Fail("空闲检测", "两条 API 都失败（会导致「仅空闲时模拟」直接放行）");
                else
                    Ok("空闲检测", "已空闲 " + (idle / 1000) + " 秒");
            }

            private static void CheckSendInput()
            {
                string err;
                if (KeepAwakeEngine.NudgeMouse(out err))
                    Ok("鼠标注入", "成功");
                else
                    Fail("鼠标注入", err);

                if (KeepAwakeEngine.NudgeKey(Native.VK_F24, out err))
                    Ok("键盘注入", "成功");
                else
                    Fail("键盘注入", err);
            }

            private static void CheckAutoStart()
            {
                try
                {
                    AutoStartInfo info = AutoStart.Query();
                    switch (info.State)
                    {
                        case AutoStartState.None:
                            Ok("状态", "未注册");
                            break;
                        case AutoStartState.Current:
                            Ok("状态", "已注册，指向当前 exe");
                            break;
                        default:
                            Line("状态", "已注册，但指向别的路径！");
                            Line("注册表值", info.Command);
                            Line("当前 exe", AutoStart.ExePath);
                            Line("后果", "开机自启会拉起一个旧版本");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail("读取", ex.Message);
                }
            }

            private static void CheckSettings()
            {
                string p = Settings.FilePath;
                Line("路径", p);
                Line("存在", File.Exists(p) ? "是" : "否（首次运行）");

                if (!File.Exists(p)) return;

                try
                {
                    string content = File.ReadAllText(p, Encoding.UTF8);
                    int lines = 0;
                    foreach (string s in content.Split('\n'))
                    {
                        if (s.Trim().Length > 0) lines++;
                    }
                    Ok("条目数", lines.ToString());
                    Line("当前配置", Settings.Current.Describe());
                }
                catch (Exception ex)
                {
                    Fail("读取", ex.Message);
                }
            }

            // --------------------------------------------------------------
            //  控制台接入
            // --------------------------------------------------------------
            private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
            private const int STD_OUTPUT_HANDLE = -11;
            private const int STD_ERROR_HANDLE  = -12;
            private const uint FILE_TYPE_CHAR    = 0x0002;
            private const uint FILE_TYPE_PIPE    = 0x0003;
            private const uint FILE_TYPE_DISK    = 0x0001;

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool AttachConsole(uint dwProcessId);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool AllocConsole();

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr GetStdHandle(int nStdHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern uint GetFileType(IntPtr h);

            /// <summary>优先复用启动时就有的 stdout / stderr（重定向场景下才拿得到输出）。</summary>
            private static TextWriter OpenOutput()
            {
                try
                {
                    foreach (int id in new[] { STD_OUTPUT_HANDLE, STD_ERROR_HANDLE })
                    {
                        IntPtr h = GetStdHandle(id);
                        if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                        uint type = GetFileType(h);
                        // 管道/文件/字符设备都行；只有「控制台」才需要 AttachConsole
                        if (type != FILE_TYPE_PIPE && type != FILE_TYPE_DISK
                            && type != FILE_TYPE_CHAR)
                            continue;

                        return new StreamWriter(NewWriter(h))
                        {
                            AutoFlush = true
                        };
                    }
                }
                catch (Exception) { }

                return null;
            }

            /// <summary>继承的句柄不可用时，借父进程的控制台。</summary>
            private static TextWriter OpenConsole()
            {
                try
                {
                    if (!AttachConsole(ATTACH_PARENT_PROCESS) && !AllocConsole())
                        return null;

                    IntPtr h = GetStdHandle(STD_OUTPUT_HANDLE);
                    if (h == IntPtr.Zero || h == new IntPtr(-1))
                        return null;

                    return new StreamWriter(NewWriter(h))
                    {
                        AutoFlush = true
                    };
                }
                catch (Exception)
                {
                    return null;
                }
            }

            /// <summary>
            /// 把裸句柄包成 FileStream。ownsHandle 必须为 false ——
            /// 那是进程启动时继承来的标准句柄，由系统负责关闭，
            /// 交给 SafeFileHandle 会在 Dispose 时把它关掉。
            /// </summary>
            private static FileStream NewWriter(IntPtr h)
            {
                return new FileStream(new SafeFileHandle(h, false),
                                      FileAccess.Write, 4096, false);
            }
        }
    }
}
