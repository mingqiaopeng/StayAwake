// ============================================================================
//  StayAwake - 托盘常驻控制器
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace StayAwake
{
    /// <summary>
    /// 无窗口托盘常驻模式。所有交互通过托盘图标与菜单完成。
    ///
    /// 「当前设置」只有 _opt 一份真值，任何入口（菜单勾选、设置面板应用、
    /// 设置面板直接关闭）都必须经过 Commit() 才能落地。原先三条平行的写盘
    /// 路径各自为政，面板关闭时不回写菜单，导致磁盘、菜单、运行中的引擎
    /// 三方状态互相矛盾。
    /// </summary>
    internal sealed class TrayController : ApplicationContext
    {
        private readonly KeepAwakeEngine _engine = new KeepAwakeEngine();
        private readonly KeepAwakeOptions _opt = new KeepAwakeOptions();

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private System.Windows.Forms.Timer _timer;

        private ToolStripMenuItem _miStatus;
        private ToolStripMenuItem _miToggle;
        private ToolStripMenuItem _miShowPanel;
        private ToolStripMenuItem _miAutoStart;
        private ToolStripMenuItem _miSimMouse;
        private ToolStripMenuItem _miSimKey;
        private ToolStripMenuItem _miOnlyIdle;
        private ToolStripMenuItem _miSleep;
        private ToolStripMenuItem _miDisplay;
        private ToolStripMenuItem _miSaver;
        private ToolStripMenuItem _miExit;

        private PanelForm _panel;
        private TrayState? _lastIconState;
        private long _lastIconBuildMs;

        private string _tooltipText;
        private bool _tooltipRunning;
        private long _tooltipMs;

        /// <summary>
        /// 托盘气泡最短刷新间隔。
        ///
        /// 气泡里带倒计时，数字每秒都在变，于是 NotifyIcon.Text 每秒都要写一次，
        /// 而每一次都是一次 Shell_NotifyIcon(NIM_MODIFY) —— 一天 8 万多次对
        /// 通知区的无意义敲打。实测 CPU 影响很小，但这是纯浪费。
        /// 倒计时的精确值改到右键菜单里给（菜单重绘不经过 shell）。
        /// </summary>
        private const long TooltipMinGapMs = 10000;

        /// <summary>
        /// 托盘图标最短重建间隔。
        ///
        /// 图标只在状态变化时重建，但状态在 Running / Warning 之间抖动时
        /// （比如 SendInput 被间歇性拦截）就变成每秒一次；碰上磁盘缓存失效，
        /// 每次重建要在 UI 线程上花 109~141ms，足以让托盘菜单彻底卡住。
        /// 加个下限，UI 最多被卡一次，之后无论状态怎么抖都只是显示得慢一点。
        /// </summary>
        private const long IconRebuildMinGapMs = 2000;

        /// <summary>由代码写 Checked 时抑制 CheckedChanged，避免自我触发。</summary>
        private bool _suppressFlagEvent;

        /// <summary>
        /// 开机自启项的重入闸门。
        ///
        /// ToolStripMenuItem.Checked 的 setter 会**再次**触发 CheckedChanged，
        /// 所以「写失败 → 回滚勾选 → 再触发一次 → 再失败 → 再回滚」会无限
        /// 递归，直到栈溢出把进程带走。原来的 _suppressFlagEvent 并没有在这里
        /// 生效 —— 它只护住了另外五个开关项。
        /// </summary>
        private bool _inAutoStartUpdate;

        public TrayController(bool autoStart)
        {
            _opt.CopyFrom(Settings.Current);

            BuildTray();

            // 启动即守护，不显示任何界面
            if (autoStart && !_engine.Running)
                StartEngine();

            UpdateTray();
        }

        private void BuildTray()
        {
            // ---- 托盘图标 ----
            _tray = new NotifyIcon();
            // CreateTrayIcon 保证不抛异常，但极端情况下可能返回 null
            Icon boot = TrayIconFactory.CreateTrayIcon(TrayState.Idle);
            _tray.Icon = boot;
            _tray.Text = "StayAwake — 未运行";
            // 与 _tray.Text 保持一致，否则第一次 UpdateTooltip 会误判成「需要更新」
            _tooltipText = _tray.Text;
            _tooltipRunning = false;
            _tooltipMs = 0;
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ShowPanel();

            // ---- 菜单 ----
            _menu = new ContextMenuStrip();
            _menu.Font = UiKit.FontUi;
            // 必须为 true 才会绘制勾选列，否则 Checked=true 也看不到勾
            _menu.ShowImageMargin = true;
            _menu.ShowCheckMargin = true;

            _miStatus = new ToolStripMenuItem("状态：未运行");
            _miStatus.Enabled = false;

            // 这一项靠文字表明状态（开始守护 / 停止守护），不需要勾号。
            // 它是命令按钮而非设置开关，CheckOnClick 保持 false。
            _miToggle = new ToolStripMenuItem("开始守护");
            _miToggle.Font = UiKit.FontUiBold;
            _miToggle.Click += (s, e) => ToggleRun();

            _miShowPanel = new ToolStripMenuItem("设置…");
            _miShowPanel.Click += (s, e) => ShowPanel();

            _menu.Items.Add(_miStatus);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miToggle);
            _menu.Items.Add(_miShowPanel);
            _menu.Items.Add(new ToolStripSeparator());

            // 这六项是纯开关：CheckOnClick 让点击直接翻转勾选，
            // 再由 CheckedChanged 统一走 Commit。
            _miSleep    = MakeToggle("阻止系统睡眠");
            _miDisplay  = MakeToggle("阻止显示器关闭");
            _miSaver    = MakeToggle("禁用屏幕保护程序");
            _miSimMouse = MakeToggle("模拟鼠标移动");
            _miSimKey   = MakeToggle("模拟键盘 (F24)");
            _miOnlyIdle = MakeToggle("仅空闲时模拟");

            _menu.Items.Add(_miSleep);
            _menu.Items.Add(_miDisplay);
            _menu.Items.Add(_miSaver);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miSimMouse);
            _menu.Items.Add(_miSimKey);
            _menu.Items.Add(_miOnlyIdle);

            _miAutoStart = new ToolStripMenuItem("开机自动启动");
            // 必须 CheckOnClick = true：点击才会翻转 Checked 并触发 CheckedChanged。
            // 若为 false 则点击不改 Checked，只监听 CheckedChanged 会变成永不触发的死代码。
            _miAutoStart.CheckOnClick = true;
            _miAutoStart.Checked = AutoStart.IsEnabled();
            _miAutoStart.CheckedChanged += (s, e) => OnAutoStartToggled(_miAutoStart.Checked);

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miAutoStart);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miExit = MakeCommand("退出"));

            _tray.ContextMenuStrip = _menu;

            // 菜单勾号初值：统一由 _opt 刷一次，同样在静默期里写，
            // 免得后续项还没初始化时把 false 当成用户选择提交出去。
            SyncMenuFromOptions();

            // ---- 定时器 ----
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        // --------------------------------------------------------------------
        //  主循环
        // --------------------------------------------------------------------
        private void OnTick(object sender, EventArgs e)
        {
            if (_engine.Running)
            {
                _engine.Tick();
                UpdateTray();
            }
        }

        private void UpdateTray()
        {
            bool running = _engine.Running;
            UpdateTooltip(running);

            _miStatus.Text = "状态：" + (running ? "守护中" : "未运行") +
                (running
                    ? string.Format("（已模拟 {0} 次 · 下次 {1}s）",
                                    _engine.NudgeCount, _engine.SecondsToNextAction)
                    : "");

            // 菜单文字与颜色统一由引擎状态派生，避免两处状态打架。
            // 刻意不设 Checked：这一项是命令按钮，状态由文字表达。
            _miToggle.Text = running ? "停止守护" : "开始守护";
            _miToggle.ForeColor = running ? UiKit.WarnRed : SystemColors.ControlText;

            // 开机自启项的勾号跟随真实注册表状态（含「指向别的路径」这种半吊子状态）
            AutoStartInfo autostart = AutoStart.Cached();
            bool autostartChecked = autostart.Registered;
            if (_inAutoStartUpdate == false && _miAutoStart.Checked != autostartChecked)
            {
                _inAutoStartUpdate = true;
                _miAutoStart.Checked = autostartChecked;
                _inAutoStartUpdate = false;
            }

            // 图标只在状态变化时重建，避免每秒泄漏 GDI 句柄
            TrayState st = CurrentIconState();
            if (_lastIconState != st &&
                Native.MonotonicMs - _lastIconBuildMs >= IconRebuildMinGapMs)
            {
                // 时间戳无论成败都要记：磁盘不可写、资源缺失导致图标造不出来时，
                // 不记就会每秒重试一次完整构建链，等于每秒卡一次 UI。
                _lastIconBuildMs = Native.MonotonicMs;

                // 状态只在真换上之后才记账，失败则下一轮自动重试（自愈）。
                if (ApplyIcon(st)) _lastIconState = st;
            }
        }

        private void UpdateTooltip(bool running)
        {
            string tip = running
                ? string.Format("StayAwake — 守护中 · 下次 {0}s", _engine.SecondsToNextAction)
                : "StayAwake — 未运行";

            // NotifyIcon.Text 上限 63 字符
            if (tip.Length > 63)
                tip = tip.Substring(0, 63);

            // 启停是状态跃迁，必须立刻反映；只是倒计时数字在走就按节流刷。
            bool forced = running != _tooltipRunning;
            if (!forced)
            {
                if (string.Equals(_tooltipText, tip, StringComparison.Ordinal)) return;
                if (Native.MonotonicMs - _tooltipMs < TooltipMinGapMs) return;
            }

            _tooltipRunning = running;
            _tooltipText = tip;
            _tooltipMs = Native.MonotonicMs;
            _tray.Text = tip;
        }

        private bool ApplyIcon(TrayState st)
        {
            Icon want = TrayIconFactory.CreateTrayIcon(st);
            if (want == null) return false;   // 保留现有图标，别把托盘弄空

            Icon old = _tray.Icon;
            _tray.Icon = want;
            if (old != null)
            {
                try { old.Dispose(); } catch { }
            }
            return true;
        }

        /// <summary>按运行状态与引擎健康度决定图标配色。</summary>
        private TrayState CurrentIconState()
        {
            if (!_engine.Running)
                return TrayState.Idle;

            // 电源请求或输入模拟失败时给出红色警示
            if (!string.IsNullOrEmpty(_engine.LastError))
                return TrayState.Warning;

            return TrayState.Running;
        }

        // --------------------------------------------------------------------
        //  设置提交（唯一入口）
        // --------------------------------------------------------------------

        /// <summary>创建一个勾选型开关项：点击即翻转勾选，变化时提交。</summary>
        private ToolStripMenuItem MakeToggle(string text)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.CheckOnClick = true;          // 点击直接翻转，勾号可见
            mi.CheckedChanged += (s, e) => OnFlagToggled();
            return mi;
        }

        private ToolStripMenuItem MakeCommand(string text)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.Click += (s, e) => ExitApp();
            return mi;
        }

        private void OnFlagToggled()
        {
            if (_suppressFlagEvent)
                return;

            ReadOptionsFromMenu();
            Commit();
        }

        private void ReadOptionsFromMenu()
        {
            _opt.PreventSleep = _miSleep.Checked;
            _opt.PreventDisplay = _miDisplay.Checked;
            _opt.DisableScreenSaver = _miSaver.Checked;
            _opt.SimMouse = _miSimMouse.Checked;
            _opt.SimKeyboard = _miSimKey.Checked;
            _opt.OnlyWhenIdle = _miOnlyIdle.Checked;
        }

        private void SyncMenuFromOptions()
        {
            _suppressFlagEvent = true;
            _miSleep.Checked = _opt.PreventSleep;
            _miDisplay.Checked = _opt.PreventDisplay;
            _miSaver.Checked = _opt.DisableScreenSaver;
            _miSimMouse.Checked = _opt.SimMouse;
            _miSimKey.Checked = _opt.SimKeyboard;
            _miOnlyIdle.Checked = _opt.OnlyWhenIdle;
            _suppressFlagEvent = false;
        }

        /// <summary>
        /// 唯一的设置提交入口：菜单勾号 → 落盘 → 让引擎生效 → 刷新托盘。
        /// 菜单、面板、磁盘三处状态由此保证不会互相矛盾。
        /// </summary>
        private void Commit()
        {
            SyncMenuFromOptions();
            Settings.Save(_opt);

            if (_engine.Running)
                _engine.Reconfigure(_opt);

            UpdateTray();
        }

        /// <summary>供 PanelForm 调用：把面板上的设置提交进来。</summary>
        internal void CommitFromPanel(KeepAwakeOptions options)
        {
            if (options == null) return;
            _opt.CopyFrom(options);
            Commit();
        }

        /// <summary>当前生效的设置（副本）。</summary>
        internal KeepAwakeOptions CurrentOptions
        {
            get { return _opt.Clone(); }
        }

        /// <summary>引擎引用，供面板显示运行状态。</summary>
        internal KeepAwakeEngine Engine
        {
            get { return _engine; }
        }

        // --------------------------------------------------------------------
        //  开机自启
        // --------------------------------------------------------------------
        private void OnAutoStartToggled(bool want)
        {
            // 闸门：无论成败都不允许在同一次点击里再进第二层
            if (_inAutoStartUpdate)
                return;

            if (AutoStart.Set(want))
            {
                Balloon("开机自启", want ? "已添加，开机后自动启动" : "已移除");
                UpdateTray();
                return;
            }

            // 写失败：把勾选改回真实状态，否则菜单在说谎。
            // 注意 Set(!want) 也可能同样失败（比如整个注册表键不可写），
            // 所以回滚时绝不能再触发一次处理逻辑 —— 那正是无限递归的来源。
            string why = string.IsNullOrEmpty(AutoStart.LastError)
                ? "未知原因" : AutoStart.LastError;

            RollBackAutoStartCheck();

            Balloon("操作失败", "无法修改启动项\n" + why);
            Log.Warn("开机自启开关失败，已回滚勾选: " + why);
        }

        private void RollBackAutoStartCheck()
        {
            _inAutoStartUpdate = true;
            _miAutoStart.Checked = AutoStart.Cached().Registered;
            _inAutoStartUpdate = false;
        }

        // --------------------------------------------------------------------
        //  启停
        // --------------------------------------------------------------------
        private void ToggleRun()
        {
            if (_engine.Running)
            {
                _engine.Stop();
            }
            else
            {
                StartEngine();
            }

            UpdateTray();
        }

        private void StartEngine()
        {
            _engine.Start(_opt);
        }

        // --------------------------------------------------------------------
        //  交互
        // --------------------------------------------------------------------
        private void Balloon(string title, string text)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(3000);
            }
            catch (Exception ex)
            {
                Log.Warn("显示气泡提示失败: " + ex.Message);
            }
        }
        internal void ShowPanel()
        {
            if (_panel != null && !_panel.IsDisposed)
            {
                // 最小化后再点「设置…」如果只 Activate()，窗口会保持最小化状态，
                // 看起来像没反应 —— 必须先还原再置前。
                if (_panel.WindowState == FormWindowState.Minimized)
                    _panel.WindowState = FormWindowState.Normal;
                _panel.Activate();
                _panel.BringToFront();
                return;
            }

            PanelForm p = new PanelForm(this);
            // 先接事件再赋值 _panel：FormClosed 里用 _panel = null 清引用，
            // 若在这里就赋值，关掉时能把新面板的引用误清成 null。
            p.FormClosed += (s, e) => { if (_panel == p) _panel = null; };
            _panel = p;
            p.Show();
            p.Activate();
        }

        private void ExitApp()
        {
            Log.Info("用户退出");
            if (_timer != null) { _timer.Stop(); }
            _engine.Stop();

            if (_panel != null && !_panel.IsDisposed)
                _panel.Close();

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
                if (_menu != null) { _menu.Dispose(); _menu = null; }
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
                _engine.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}