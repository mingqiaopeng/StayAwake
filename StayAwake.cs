// ============================================================================
//  StayAwake - 防止系统自动锁屏 / 休眠
//
//  Win32 互操作 + 配置对象 + 核心引擎。
//  编译: build.cmd
// ============================================================================

using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("StayAwake")]
[assembly: AssemblyProduct("StayAwake")]
[assembly: AssemblyDescription("Prevent automatic lock screen and sleep on Windows")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyCopyright("Copyright (c) 2026 StayAwake contributors")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace StayAwake
{
    // ========================================================================
    //  Win32 互操作层
    // ========================================================================
    internal static class Native
    {
        // ---- SetThreadExecutionState ----
        internal const uint ES_CONTINUOUS      = 0x80000000;
        internal const uint ES_SYSTEM_REQUIRED  = 0x00000001;   // 阻止系统休眠
        internal const uint ES_DISPLAY_REQUIRED = 0x00000002;   // 阻止显示器关闭

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint SetThreadExecutionState(uint esFlags);

        // ---- 屏幕保护程序开关 ----
        // 常量取自 winuser.h：SET 是 0x0011，GET 是 0x0010（不是 0x0012）。
        // GET 必须配一个真实的 BOOL* 才能拿到旧值 —— 退出时原样写回的前提。
        private const uint SPI_GETSCREENSAVEACTIVE = 0x0010;
        private const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE    = 0x02;

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam,
            IntPtr pvParam, uint fWinIni);

        /// <summary>读取屏幕保护程序当前是否启用。失败返回 false（此时不该去改它）。</summary>
        internal static bool TryGetScreenSaverEnabled(out bool enabled)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                p = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(p, 0);
                if (SystemParametersInfo(SPI_GETSCREENSAVEACTIVE, 0, p, 0))
                {
                    enabled = Marshal.ReadInt32(p) != 0;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取屏保状态失败: " + ex.Message);
            }
            finally
            {
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            }
            enabled = false;
            return false;
        }

        /// <summary>启用/禁用屏幕保护程序。</summary>
        internal static bool SetScreenSaverEnabled(bool enabled)
        {
            try
            {
                return SystemParametersInfo(SPI_SETSCREENSAVEACTIVE,
                    enabled ? 1u : 0u, IntPtr.Zero,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            catch (Exception ex)
            {
                Log.Warn("设置屏保状态失败: " + ex.Message);
                return false;
            }
        }

        // ---- 空闲时间检测 ----
        //
        // 只有 GetLastInputInfo 这一个 API。user32.dll 并不导出
        // 「GetLastInputInfoEx」—— 那是本项目的臆造，MSDN 与 pinvoke.net
        // 都只有下面这个 Win2000 起的版本。曾为那个不存在的导出维护过
        // 一整套「64 位时基优先 + 降级」机制，实际每次都在抛
        // EntryPointNotFoundException 后走降级分支，白白多一次异常构造。
        // 已删除。
        //
        // dwTime 是 GetTickCount() 的 32 位值，约 49.7 天回绕一次。
        // 无符号相减天然处理回绕，见 GetIdleMilliseconds()。
        [StructLayout(LayoutKind.Sequential)]
        internal struct LASTINPUTINFO
        {
            internal uint cbSize;
            internal uint dwTime;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetLastInputInfo(ref LASTINPUTINFO pli);

        // Marshal.SizeOf(typeof(T)) 每次调用都要装箱一个 Type 再走一次运行时
        // 查找。空闲检测每轮都用，缓存成 static readonly 即可 ——
        // 结构体布局在进程生命周期内是常量，结果不可能变。
        internal static readonly uint SizeOfLastInputInfo =
            (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));

        [DllImport("kernel32.dll")]
        internal static extern long GetTickCount64();

        /// <summary>
        /// 单调时钟基准。调度一律用它，不用 DateTime.Now ——
        /// NTP 回拨或用户手改时间会让墙钟倒着走，倒计时就永远等不到了。
        /// </summary>
        internal static long MonotonicMs { get { return GetTickCount64(); } }

        // ---- SendInput ----
        internal const uint INPUT_MOUSE    = 0;
        internal const uint INPUT_KEYBOARD = 1;

        internal const uint MOUSEEVENTF_MOVE = 0x0001;
        internal const uint KEYEVENTF_KEYUP  = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            internal int dx;
            internal int dy;
            internal uint mouseData;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            internal ushort wVk;
            internal ushort wScan;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct INPUTUNION
        {
            [FieldOffset(0)] internal MOUSEINPUT mi;
            [FieldOffset(0)] internal KEYBDINPUT ki;
            [FieldOffset(0)] internal HID_INPUT_SIZED hi;
        }

        // 占位类型：确保 union 至少有 HW 成员大小（x64 下 8 字节），实际不使用
        [StructLayout(LayoutKind.Sequential)]
        internal struct HID_INPUT_SIZED
        {
            internal uint a;
            internal uint b;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            internal uint type;
            internal INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        internal static int InputSize
        {
            get { return Marshal.SizeOf(typeof(INPUT)); }
        }

        /// <summary>把 Win32 错误码翻成人话，SendInput 失败时全靠它定位。</summary>
        internal static string LastWin32Error()
        {
            // 必须用 Marshal.GetLastWin32Error()。
            //
            // 早先是自己 [DllImport("kernel32.dll", SetLastError = true)]
            // 声明 GetLastError 再直接调用 —— 这是反模式：带 SetLastError
            // 的导入，CLR 会在**调用前**保存当前错误码、**调用后**再把它还原，
            // 而 GetLastError() 返回的正是那个被还原掉的槽位值。
            // 换句话说，这个声明恰好在读它的那一刻把要读的值覆盖了。
            uint code = (uint)Marshal.GetLastWin32Error();
            if (code == 0) return "无错误信息";
            return string.Format("{0} (0x{1:X})", new Win32Exception((int)code).Message, code);
        }

        // ---- 模拟按键用虚拟键码 ----
        /// <summary>F24：没有绑定任何默认操作的按键，注入后不留副作用。</summary>
        internal const ushort VK_F24 = 0x87;
    }

    // ========================================================================
    //  配置对象
    // ========================================================================
    /// <summary>
    /// 全部可调项的快照。引擎只认这个对象，调用方不再有机会传出互相矛盾的参数。
    /// </summary>
    internal sealed class KeepAwakeOptions
    {
        internal bool PreventSleep = true;
        internal bool PreventDisplay = true;
        internal bool DisableScreenSaver = false;
        internal bool SimMouse = true;
        internal bool SimKeyboard = true;
        internal bool OnlyWhenIdle = true;
        internal int IntervalSeconds = 30;

        /// <summary>模拟键盘时使用的虚拟键码。默认 F24 —— 几乎没有任何程序绑定它。</summary>
        internal ushort NudgeVirtualKey = Native.VK_F24;

        internal KeepAwakeOptions Clone()
        {
            KeepAwakeOptions c = new KeepAwakeOptions();
            c.CopyFrom(this);
            return c;
        }

        internal void CopyFrom(KeepAwakeOptions o)
        {
            if (o == null) return;
            PreventSleep = o.PreventSleep;
            PreventDisplay = o.PreventDisplay;
            DisableScreenSaver = o.DisableScreenSaver;
            SimMouse = o.SimMouse;
            SimKeyboard = o.SimKeyboard;
            OnlyWhenIdle = o.OnlyWhenIdle;
            IntervalSeconds = ClampInterval(o.IntervalSeconds);
            NudgeVirtualKey = o.NudgeVirtualKey;
        }

        internal static int ClampInterval(int v)
        {
            return v < 5 ? 5 : (v > 3600 ? 3600 : v);
        }

        /// <summary>
        /// 是否属于「改动后必须重启引擎」的那部分。
        /// 只开/关模拟、只切空闲判定不影响已经提交给系统的电源请求，
        /// 没必要重启 —— 重启会把倒计时清零，等于白等一整个间隔。
        /// </summary>
        internal bool SameSystemState(KeepAwakeOptions o)
        {
            return o != null
                && PreventSleep == o.PreventSleep
                && PreventDisplay == o.PreventDisplay
                && DisableScreenSaver == o.DisableScreenSaver
                && IntervalSeconds == ClampInterval(o.IntervalSeconds);
        }

        /// <summary>给日志 / 自检用的可读描述。</summary>
        internal string Describe()
        {
            return string.Format(
                "睡眠={0} 息屏={1} 屏保禁用={2} 鼠标={3} 键盘={4} 仅空闲={5} 间隔={6}s",
                PreventSleep, PreventDisplay, DisableScreenSaver,
                SimMouse, SimKeyboard, OnlyWhenIdle, IntervalSeconds);
        }
    }

    // ========================================================================
    //  核心引擎
    // ========================================================================
    internal sealed class KeepAwakeEngine : IDisposable
    {
        /// <summary>
        /// 「仅空闲时模拟」的判定阈值：系统连续无输入超过这么久才注入。
        ///
        /// 刻意与 IntervalSeconds 解耦。早先的写法拿间隔当阈值，
        /// 判定就只靠 1 秒轮询「迟到」那点余量撑着 —— 定时器早触发几毫秒就会
        /// 静默跳过一轮，用户只会看到「已模拟 N 次」不涨，却查不出原因。
        /// </summary>
        internal const long IdleThresholdMs = 15000;

        private readonly object _sync = new object();

        private KeepAwakeOptions _opt = new KeepAwakeOptions();

        private bool _running;
        private long _startedMs;
        private long _nextTickMs;

        private long _nudgeCount;
        private long _idleSkipCount;
        private long _nudgeFailCount;

        private string _powerError;
        private string _nudgeError;        // 用于去重，避免同一条错误刷爆日志
        private string _lastNudgeMsg;

        // 屏保：记下改之前的原值，退出时原样写回，而不是无脑设成「启用」。
        // 无脑设成启用会覆盖用户本来就关屏保的偏好。
        private bool _saverPatched;
        private bool _saverWasEnabled;

        // --------------------------------------------------------------------
        //  退出兜底
        // --------------------------------------------------------------------
        // RestoreSaverLocked() 只在 Stop()/Dispose() 里跑，而这两条路都要求
        // 进程「干干净净地退出」。任务管理器强杀、注销、关机、断电、崩溃
        // 都不会走到 —— 屏保就永久停在禁用状态，并跨重启保留。
        // 这里另存一份进程级快照，交给 ProcessExit / SessionEnding 兜底。
        private static readonly object ExitSync = new object();
        private static bool _exitSaverPatched;
        private static bool _exitSaverWasEnabled;
        private static bool _exitGuardInstalled;

        public KeepAwakeEngine()
        {
            InstallExitGuard();
        }

        private static void InstallExitGuard()
        {
            lock (ExitSync)
            {
                if (_exitGuardInstalled) return;
                _exitGuardInstalled = true;
            }

            // 正常退出 / Environment.Exit / 未捕获异常走到 CLR 收尾时触发。
            // 注意：这也覆盖不了 TerminateProcess（任务管理器 End task、
            // kill、End Process）—— 那种情况下进程连收尾代码都跑不到，
            // 任何应用都无能为力，能覆盖注销和关机已经是能做到的上限。
            try { AppDomain.CurrentDomain.ProcessExit += OnProcessExit; }
            catch (Exception ex) { Log.Warn("挂接 ProcessExit 失败: " + ex.Message); }

            // 注销 / 关机：WinForms 退出流程不保证会走到 Engine.Stop()，
            // 这条是实践中真正会触发的那个。
            try { Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding; }
            catch (Exception ex) { Log.Warn("挂接 SessionEnding 失败: " + ex.Message); }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            RestoreSaverOnExit("进程退出");
        }

        private static void OnSessionEnding(
            object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            // 不区分注销 / 关机：恢复动作完全一样，少依赖一个枚举类型。
            Log.Info("系统即将注销或关机，提前恢复系统设置");
            RestoreSaverOnExit("系统注销/关机");
        }

        /// <summary>
        /// 退出路径专用的恢复。只动静态快照，不碰实例字段 ——
        /// 此刻引擎可能正在被别处 Dispose，再去抢 _sync 只会平添死锁风险。
        /// </summary>
        private static void RestoreSaverOnExit(string why)
        {
            bool back;
            lock (ExitSync)
            {
                if (!_exitSaverPatched) return;
                back = _exitSaverWasEnabled;
                _exitSaverPatched = false;   // 先清标志：失败也不反复重试
            }

            try
            {
                if (Native.SetScreenSaverEnabled(back))
                    Log.Info(why + "兜底：已恢复屏幕保护程序状态: " + (back ? "启用" : "禁用"));
                else
                    Log.Warn(why + "兜底：恢复屏幕保护程序状态失败: " + Native.LastWin32Error());
            }
            catch (Exception ex)
            {
                Log.Warn(why + "兜底失败: " + ex.Message);
            }
        }

        /// <summary>屏保已被改动 —— 同时登记到退出兜底快照。</summary>
        private void MarkSaverPatchedLocked(bool wasEnabled)
        {
            _saverPatched = true;
            _saverWasEnabled = wasEnabled;
            lock (ExitSync)
            {
                _exitSaverPatched = true;
                _exitSaverWasEnabled = wasEnabled;
            }
        }

        /// <summary>屏保已恢复（或确认无需恢复）—— 退出兜底快照同步撤销。</summary>
        private void MarkSaverRestored()
        {
            _saverPatched = false;
            lock (ExitSync)
            {
                _exitSaverPatched = false;
                _exitSaverWasEnabled = false;
            }
        }

        public bool Running { get { lock (_sync) { return _running; } } }

        public KeepAwakeOptions Options
        {
            get { lock (_sync) { return _opt.Clone(); } }
        }

        /// <summary>已运行时长（单调时钟算出，不受改系统时间影响）。</summary>
        public TimeSpan RunningFor
        {
            get
            {
                lock (_sync)
                {
                    return _running
                        ? TimeSpan.FromMilliseconds(Native.MonotonicMs - _startedMs)
                        : TimeSpan.Zero;
                }
            }
        }

        /// <summary>距离下一次动作还有几秒（向上取整，不可用时为 0）。</summary>
        public int SecondsToNextAction
        {
            get
            {
                lock (_sync)
                {
                    if (!_running) return 0;
                    long d = _nextTickMs - Native.MonotonicMs;
                    if (d <= 0) return 0;
                    return (int)((d + 999) / 1000);
                }
            }
        }

        public long NudgeCount { get { lock (_sync) { return _nudgeCount; } } }
        public long IdleSkipCount { get { lock (_sync) { return _idleSkipCount; } } }
        public long NudgeFailCount { get { lock (_sync) { return _nudgeFailCount; } } }

        /// <summary>最近一次故障描述；一切正常时为 null。驱动托盘红色告警。</summary>
        public string LastError
        {
            get { lock (_sync) { return _powerError ?? _nudgeError; } }
        }

        // --------------------------------------------------------------------
        //  空闲检测
        // --------------------------------------------------------------------

        /// <summary>GetLastInputInfo 是否可用；一旦抛过异常就别再试了。</summary>
        // volatile：GetIdleMilliseconds 是 static，会被托盘线程和 --silent
        // 工作线程同时调用。标志虽是幂等闩锁，但跨线程读写不带屏障在
        // 内存模型上没有保证。
        private static volatile bool _idleApiAvailable = true;

        /// <summary>
        /// 当前已空闲毫秒数。拿不到时返回 -1（未知）。
        ///
        /// 早先所有失败路径都 return 0，而 0 在「仅空闲时模拟」里等于
        /// 「用户刚刚动过鼠标」—— 于是每一次都被跳过，模拟永久停摆且
        /// 毫无提示。失败必须是「未知」，由调用方决定放行。
        /// </summary>
        internal static long GetIdleMilliseconds()
        {
            // 标志位必须真的挡住调用 —— 只在 catch 里置位是不够的：
            // 下一轮照样去撞同一个缺失的导出，又抛一次异常、又写一条日志，
            // 实测 335KB/天，36 小时就把 512KB 日志额度冲光。
            if (!_idleApiAvailable) return -1;

            try
            {
                Native.LASTINPUTINFO info = new Native.LASTINPUTINFO();
                info.cbSize = Native.SizeOfLastInputInfo;
                if (Native.GetLastInputInfo(ref info))
                {
                    // 无符号减法天然处理 49.7 天的 32 位回绕。
                    // 取低 32 位与被减数是同一时基，差值正确。
                    uint delta = unchecked((uint)Native.MonotonicMs - info.dwTime);
                    return delta;
                }

                // 返回 false 是「这次调用失败」，不是「API 不可用」，
                // 因此不能清掉 _idleApiAvailable。
                return -1;
            }
            catch (Exception ex)
            {
                _idleApiAvailable = false;
                Log.Warn("GetLastInputInfo 不可用（" + ex.GetType().Name
                         + "），「仅空闲时模拟」将退化为始终注入");
                return -1;
            }
        }

        // --------------------------------------------------------------------
        //  鼠标 / 键盘模拟
        // --------------------------------------------------------------------

        /// <summary>鼠标相对移动 1 像素后立即移回，视觉上无位移。</summary>
        internal static bool NudgeMouse(out string error)
        {
            error = null;
            try
            {
                Native.INPUT[] inputs = new Native.INPUT[2];

                inputs[0].type = Native.INPUT_MOUSE;
                inputs[0].u.mi.dx = 1;
                inputs[0].u.mi.dy = 0;
                inputs[0].u.mi.mouseData = 0;
                inputs[0].u.mi.dwFlags = Native.MOUSEEVENTF_MOVE;
                inputs[0].u.mi.time = 0;
                inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

                inputs[1].type = Native.INPUT_MOUSE;
                inputs[1].u.mi.dx = -1;
                inputs[1].u.mi.dy = 0;
                inputs[1].u.mi.mouseData = 0;
                inputs[1].u.mi.dwFlags = Native.MOUSEEVENTF_MOVE;
                inputs[1].u.mi.time = 0;
                inputs[1].u.mi.dwExtraInfo = IntPtr.Zero;

                uint sent = Native.SendInput(2, inputs, Native.InputSize);
                if (sent == 2) return true;

                error = DescribeSendInputFailure(sent);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>按下并释放一个无副作用的按键（F24），不会输入任何字符。</summary>
        internal static bool NudgeKey(ushort virtualKey, out string error)
        {
            error = null;
            try
            {
                Native.INPUT[] inputs = new Native.INPUT[2];

                inputs[0].type = Native.INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = virtualKey;
                inputs[0].u.ki.wScan = 0;
                inputs[0].u.ki.dwFlags = 0;
                inputs[0].u.ki.time = 0;
                inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

                inputs[1].type = Native.INPUT_KEYBOARD;
                inputs[1].u.ki.wVk = virtualKey;
                inputs[1].u.ki.wScan = 0;
                inputs[1].u.ki.dwFlags = Native.KEYEVENTF_KEYUP;
                inputs[1].u.ki.time = 0;
                inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

                uint sent = Native.SendInput(2, inputs, Native.InputSize);
                if (sent == 2) return true;

                error = DescribeSendInputFailure(sent);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// SendInput 返回 0 最常见的两种原因：前台窗口权限更高被 UIPI 挡住，
        /// 或者安全软件 hook 拦截。这个提示是用户唯一能看见的线索。
        /// </summary>
        private static string DescribeSendInputFailure(uint sent)
        {
            string baseMsg = "SendInput 仅提交 " + sent + "/2 个事件: " + Native.LastWin32Error();
            return baseMsg + "（若前台是管理员权限程序，此为 UIPI 拦截；亦可能被安全软件拦截）";
        }

        /// <summary>释放所有电源/屏幕请求。</summary>
        internal static void ReleaseAll()
        {
            try
            {
                Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            }
            catch (Exception ex)
            {
                Log.Warn("释放电源请求失败: " + ex.Message);
            }
        }

        // --------------------------------------------------------------------
        //  生命周期
        // --------------------------------------------------------------------

        public void Start(KeepAwakeOptions options)
        {
            lock (_sync)
            {
                if (_running)
                {
                    ReconfigureLocked(options);
                    return;
                }

                _opt = options == null ? new KeepAwakeOptions() : options.Clone();

                _running = true;
                _startedMs = Native.MonotonicMs;
                _nudgeCount = 0;
                _idleSkipCount = 0;
                _nudgeFailCount = 0;
                _powerError = null;
                _nudgeError = null;
                _lastNudgeMsg = null;

                ApplySaverLocked();
                _nextTickMs = Native.MonotonicMs + IntervalMsLocked();

                // 立刻提交一次，随后由定时器周期续期
                ApplyExecutionStateLocked();

                Log.Info("开始守护 | " + _opt.Describe());

                if (!_opt.PreventSleep && !_opt.PreventDisplay
                    && !_opt.SimMouse && !_opt.SimKeyboard)
                {
                    Log.Warn("所有能力均为关闭状态，守护进程实际上不会产生任何效果");
                }
            }
        }

        /// <summary>
        /// 应用新配置。只有影响系统状态的部分变了才真正重启引擎；
        /// 否则就地更新并保留当前倒计时。
        /// </summary>
        public void Reconfigure(KeepAwakeOptions options)
        {
            lock (_sync)
            {
                if (!_running)
                {
                    _opt = options == null ? new KeepAwakeOptions() : options.Clone();
                    return;
                }
                ReconfigureLocked(options);
            }
        }

        private void ReconfigureLocked(KeepAwakeOptions options)
        {
            KeepAwakeOptions next = options == null ? new KeepAwakeOptions() : options.Clone();

            if (_opt.SameSystemState(next))
            {
                // 能走到这里就说明 DisableScreenSaver 必然一致（SameSystemState 已比对过），
                // 所以只剩模拟策略变了：就地换掉，不动倒计时。
                _opt = next;
                Log.Debug("配置已更新（无需重启引擎）| " + _opt.Describe());
                return;
            }

            Log.Debug("配置变更涉及系统状态，重启引擎 | " + next.Describe());

            RestoreSaverLocked();
            _opt = next;

            ApplySaverLocked();
            _nextTickMs = Native.MonotonicMs + IntervalMsLocked();
            ApplyExecutionStateLocked();
        }

        /// <summary>停止守护并原样恢复被改动过的系统设置。可重复调用。</summary>
        public void Stop()
        {
            lock (_sync)
            {
                bool was = _running;
                _running = false;
                _nextTickMs = 0;

                ReleaseAll();
                RestoreSaverLocked();

                if (was)
                    Log.Info("已停止守护 | 累计模拟 " + _nudgeCount
                             + " 次，空闲跳过 " + _idleSkipCount
                             + " 次，失败 " + _nudgeFailCount + " 次");
            }
        }

        private long IntervalMsLocked()
        {
            return (long)KeepAwakeOptions.ClampInterval(_opt.IntervalSeconds) * 1000L;
        }

        private void ApplySaverLocked()
        {
            if (_opt.DisableScreenSaver)
            {
                if (_saverPatched) return;

                bool was;
                if (!Native.TryGetScreenSaverEnabled(out was))
                {
                    Log.Warn("读不到屏保原状态，已放弃禁用屏保（退出时也就无从恢复）");
                    return;
                }

                // 原值交给 MarkSaverPatchedLocked 一并登记（实例 + 退出兜底快照），
                // 不在这里单独写 _saverWasEnabled，避免两处状态各写一半。
                if (!was)
                {
                    // 本来就没开屏保，什么都不用改；但仍标记为 patched，
                    // 这样 Stop 时会把「关闭」这个原值再写回去（幂等，也防中途被别的程序打开）。
                    MarkSaverPatchedLocked(was);
                    Log.Debug("屏保本就是关闭状态，无需改动");
                    return;
                }

                if (Native.SetScreenSaverEnabled(false))
                {
                    MarkSaverPatchedLocked(was);
                    Log.Info("已临时禁用屏幕保护程序（退出时恢复）");
                }
                else
                {
                    Log.Warn("禁用屏幕保护程序失败: " + Native.LastWin32Error());
                }
            }
            else
            {
                RestoreSaverLocked();
            }
        }

        private void RestoreSaverLocked()
        {
            if (!_saverPatched) return;

            bool back = _saverWasEnabled;
            MarkSaverRestored();   // 先清标志：即使写入失败也不反复重试

            if (Native.SetScreenSaverEnabled(back))
                Log.Info("已恢复屏幕保护程序状态: " + (back ? "启用" : "禁用"));
            else
                Log.Warn("恢复屏幕保护程序状态失败: " + Native.LastWin32Error());
        }

        private void ApplyExecutionStateLocked()
        {
            try
            {
                uint flags = Native.ES_CONTINUOUS;
                if (_opt.PreventSleep)
                    flags |= Native.ES_SYSTEM_REQUIRED;
                if (_opt.PreventDisplay)
                    flags |= Native.ES_DISPLAY_REQUIRED;

                uint r = Native.SetThreadExecutionState(flags);

                if (r == 0)
                {
                    SetPowerError("SetThreadExecutionState 返回 0，电源请求未生效");
                }
                else
                {
                    if (_powerError != null)
                        Log.Info("电源请求已恢复");
                    _powerError = null;
                }
            }
            catch (Exception ex)
            {
                SetPowerError("调用 SetThreadExecutionState 失败: " + ex.Message);
            }
        }

        private void SetPowerError(string msg)
        {
            bool changed = !string.Equals(_powerError, msg, StringComparison.Ordinal);
            _powerError = msg;
            if (changed) Log.Warn(msg);
        }

        private void SetNudgeError(string what, string detail)
        {
            _nudgeFailCount++;
            _nudgeError = what + "模拟失败: " + detail;

            // 同一条错误只记一次，避免每轮把日志刷满
            Log.Once(ref _lastNudgeMsg, _nudgeError);
        }

        /// <summary>定时器回调。返回 true 表示本轮至少做了一次动作。</summary>
        public bool Tick()
        {
            lock (_sync)
            {
                if (!_running)
                    return false;

                long now = Native.MonotonicMs;
                if (now < _nextTickMs)
                    return false;

                _nextTickMs = now + IntervalMsLocked();

                // 1) 续期电源请求
                ApplyExecutionStateLocked();

                // 2) 若系统还在被使用，跳过模拟（否则会打断你正在做的事）
                if (_opt.OnlyWhenIdle)
                {
                    long idle = GetIdleMilliseconds();
                    if (idle >= 0 && idle < IdleThresholdMs)
                    {
                        _idleSkipCount++;
                        Log.Debug("系统仍在活动（空闲 " + idle + "ms），跳过本轮模拟");
                        return false;
                    }
                    if (idle < 0)
                    {
                        // 读不到空闲时间时放行，而不是当成「刚刚有人动过」永久停摆。
                        Log.Debug("读不到系统空闲时间，本轮放行");
                    }
                }

                // 3) 发送模拟输入，重置系统空闲计时
                bool did = false;

                if (_opt.SimMouse)
                {
                    string err;
                    if (NudgeMouse(out err)) did = true;
                    else SetNudgeError("鼠标", err);
                }

                if (_opt.SimKeyboard)
                {
                    string err;
                    if (NudgeKey(_opt.NudgeVirtualKey, out err)) did = true;
                    else SetNudgeError("键盘", err);
                }

                if (did)
                {
                    _nudgeCount++;
                    if (_nudgeError != null)
                    {
                        Log.Info("模拟输入已恢复正常");
                        _nudgeError = null;
                        _lastNudgeMsg = null;
                    }
                    Log.Debug("第 " + _nudgeCount + " 次模拟完成");
                }

                return did;
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}