// ============================================================================
//  StayAwake - 运行日志
//
//  目的：ES 电源请求失败、SendInput 被 UIPI 拦下、注册表写不进去 ——
//  这些事实在 UI 上要么只是「计数不涨」，要么干脆没提示。没有日志就等于瞎猜。
//
//  位置：%LOCALAPPDATA%\StayAwake\stayawake.log
//  超过 512KB 就丢掉前半截，保证单文件且不会无限膨胀。
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace StayAwake
{
    internal enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// 极简滚动文件日志。写日志本身的任何失败都被吞掉 ——
    /// 日志是诊断手段，绝不能反过来把主程序搞崩。
    /// </summary>
    internal static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private const string FileName = "stayawake.log";

        /// <summary>
        /// 连续失败多少次才永久停用。
        ///
        /// 原先任意一次失败就 _disabled = true，永不恢复。而写日志失败的常见
        /// 原因是瞬时的 —— 杀软正在扫文件、清理软件正在搬目录、磁盘瞬时写满。
        /// 为了一次瞬时抖动赔上整个进程生命周期的日志，代价太大：真出故障时
        /// 恰恰是最需要日志的时候。
        /// </summary>
        private const int MaxConsecutiveFailures = 5;

        private static readonly object Sync = new object();
        private static string _path;
        private static bool _disabled;
        private static int _failCount;

        /// <summary>低于此级别不落盘。默认 Info；--verbose 会开到 Debug。</summary>
        internal static LogLevel MinLevel = LogLevel.Info;

        internal static void SetVerbose(bool verbose)
        {
            MinLevel = verbose ? LogLevel.Debug : LogLevel.Info;
        }

        internal static string FilePath
        {
            get
            {
                if (_path != null || _disabled) return _path;
                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "StayAwake");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, FileName);
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    DebugOut("日志目录不可用，已禁用日志: " + ex.Message);
                }
                return _path;
            }
        }

        internal static void Debug(string msg) { Write(LogLevel.Debug, msg, null); }
        internal static void Info(string msg) { Write(LogLevel.Info, msg, null); }
        internal static void Warn(string msg) { Write(LogLevel.Warn, msg, null); }

        internal static void Error(string msg, Exception ex)
        {
            Write(LogLevel.Error, msg, ex);
        }

        /// <summary>记录「某种错误持续存在」的首次出现，同一条消息不重复刷屏。</summary>
        internal static void Once(ref string seen, string msg)
        {
            if (string.Equals(seen, msg, StringComparison.Ordinal)) return;
            seen = msg;
            Write(LogLevel.Warn, msg, null);
        }

        private static void Write(LogLevel level, string msg, Exception ex)
        {
            if (_disabled || level < MinLevel) return;

            try
            {
                string path = FilePath;
                if (string.IsNullOrEmpty(path)) return;

                StringBuilder sb = new StringBuilder(160);
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff",
                                                CultureInfo.InvariantCulture));
                sb.Append(" [").Append(Tag(level)).Append("] ");
                sb.Append(msg);
                if (ex != null)
                    sb.Append(" | ").Append(ex.GetType().Name)
                      .Append(": ").Append(ex.Message);
                sb.Append(Environment.NewLine);

                lock (Sync)
                {
                    Trim(path);
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                    _failCount = 0;
                }
            }
            catch
            {
                // 日志失败不影响主流程。连续失败才停用 —— 单次瞬时失败
                // 不该让整个进程后面的日志全部消失。
                lock (Sync)
                {
                    _failCount++;
                    if (_failCount >= MaxConsecutiveFailures && !_disabled)
                    {
                        _disabled = true;
                        DebugOut("日志连续写入失败 " + _failCount + " 次，已停用日志");
                    }
                }
            }
        }

        private static void Trim(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists || fi.Length <= MaxBytes) return;

                // 先把要保留的尾巴读进内存并释放句柄，再覆写 ——
                // 边持有读句柄边 File.WriteAllText 会撞上共享模式。
                string tail;
                using (FileStream fs = new FileStream(path, FileMode.Open,
                                                      FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(fi.Length - MaxBytes / 2, SeekOrigin.Begin);
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        sr.ReadLine();   // 丢掉被截断的半行
                        tail = sr.ReadToEnd();
                    }
                }
                File.WriteAllText(path, "…(旧记录已截断)…\r\n" + tail, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string Tag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DBG";
                case LogLevel.Warn: return "WRN";
                case LogLevel.Error: return "ERR";
                default: return "INF";
            }
        }

        /// <summary>不依赖日志文件的最后手段。</summary>
        private static void DebugOut(string text)
        {
            try { Console.Error.WriteLine("[StayAwake] " + text); }
            catch { }
        }
    }
}