// ============================================================================
//  StayAwake - 托盘模式的设置面板（按需弹出，不常驻）
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace StayAwake
{
    /// <summary>
    /// 托盘模式的设置窗口：仅在用户点击「设置…」时弹出。
    ///
    /// 面板本身不碰引擎、也不写盘 —— 它只把控件上的值收集成
    /// KeepAwakeOptions 交给 TrayController.Commit()。
    /// 这样「应用」和「直接关窗」走的是同一条路，不会再出现
    /// 磁盘改了、菜单没改、引擎还在用旧值的三方分裂。
    /// </summary>
    internal sealed class PanelForm : Form
    {
        private readonly TrayController _tray;
        private readonly KeepAwakeEngine _engine;
        private readonly KeepAwakeOptions _initial;

        private readonly CheckBox _chkSleep;
        private readonly CheckBox _chkDisplay;
        private readonly CheckBox _chkSaver;
        private readonly CheckBox _chkSimMouse;
        private readonly CheckBox _chkSimKey;
        private readonly CheckBox _chkOnlyIdle;
        private readonly NumericUpDown _numInterval;
        private readonly Label _lblState;
        private readonly System.Windows.Forms.Timer _timer;
        private bool _applied;

        public PanelForm(TrayController tray)
        {
            if (tray == null) throw new ArgumentNullException("tray");

            _tray = tray;
            _engine = tray.Engine;
            _initial = tray.CurrentOptions;

            Text = "StayAwake 设置";
            StartPosition = FormStartPosition.CenterScreen;
            // AutoScaleMode.Dpi + 96 基准：150% 缩放下 WinForms 会按实际 DPI
            // 等比放大所有子控件坐标，原先写死 ClientSize 的做法在高分屏上必然错位。
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            ClientSize = new Size(430, 376);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(0xF2, 0xF4, 0xF7);
            Font = UiKit.FontUi;
            Icon = TrayIconFactory.CreateAppIcon(32);

            // ---- 状态行 ----
            _lblState = UiKit.MakeLabelBoxed("未运行", 11F, FontStyle.Bold,
                                              UiKit.IdleGrey, Pad, 16, ClientW, 24);

            // ---- 开关组 ----
            // 布局全程用「跑动 y」推进，每放一个控件就 y += 它的高度 + 间距，
            // 不再混用写死的行号 —— 原来按钮写死 y=310，正好压在 y=300 的提示
            // 文字上（提示占 300~336）。
            int y = 52;

            y = AddSection("保持唤醒", y);
            _chkSleep = AddCheck("阻止系统进入睡眠", _initial.PreventSleep, y);
            _chkDisplay = AddCheck("阻止显示器关闭", _initial.PreventDisplay, y + 26);
            _chkSaver = AddCheck("禁用屏幕保护程序（退出时自动恢复原状态）",
                                 _initial.DisableScreenSaver, y + 52);
            y += 84;

            y = AddSection("模拟用户活动（防锁屏）", y);
            _chkSimMouse = AddCheck("发送鼠标移动消息（1px 往返）", _initial.SimMouse, y);
            _chkSimKey = AddCheck("发送键盘消息（F24，无副作用）", _initial.SimKeyboard, y + 26);
            _chkOnlyIdle = AddCheck("仅在系统空闲时模拟（推荐）", _initial.OnlyWhenIdle, y + 52);
            y += 84;

            // 间隔
            Controls.Add(UiKit.MakeLabel("间隔", 9F, FontStyle.Bold,
                                         UiKit.TextMain, Pad, y + 3));
            _numInterval = new NumericUpDown
            {
                Location = new Point(Pad + 42, y),
                Width = 70,
                Minimum = 5,
                Maximum = 3600,
                Value = KeepAwakeOptions.ClampInterval(_initial.IntervalSeconds)
            };
            Controls.Add(_numInterval);
            Controls.Add(UiKit.MakeLabel("秒（5 ~ 3600）", 9F, FontStyle.Regular,
                                         UiKit.TextSub, Pad + 122, y + 3));
            y += 32;

            Label tip = UiKit.MakeLabelBoxed(
                "改完点「应用」立即生效；直接关窗也会自动保存。\r\n" +
                "托盘图标颜色：蓝=守护中，灰=未运行，红=发生故障。",
                9F, FontStyle.Regular, UiKit.TextSub, Pad, y, ClientW, 38);
            Controls.Add(tip);
            y += 38 + 16;

            // ---- 按钮 ----
            Button btnApply = UiKit.MakeButton("应用", Pad, y, 90, 32,
                                               UiKit.Accent, Color.White, false);
            btnApply.Click += (s, e) => { Apply(); _applied = true; };

            Button btnClose = UiKit.MakeButton("关闭", Pad + 100, y, 90, 32,
                                               Color.White, UiKit.TextMain, true);
            btnClose.Click += (s, e) => Close();

            // 底边留白：按钮下方固定 20px，不贴边
            ClientSize = new Size(ClientW, y + 32 + 20);

            Controls.Add(_lblState);
            Controls.Add(_chkSleep);
            Controls.Add(_chkDisplay);
            Controls.Add(_chkSaver);
            Controls.Add(_chkSimMouse);
            Controls.Add(_chkSimKey);
            Controls.Add(_chkOnlyIdle);
            Controls.Add(btnApply);
            Controls.Add(btnClose);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => RefreshState();
            _timer.Start();

            FormClosing += OnClosing;
            RefreshState();
        }

        private const int Pad = 24;
        private const int ClientW = 386;

        /// <summary>放一个分组标题，返回标题下方应继续排布的 y。</summary>
        private int AddSection(string text, int y)
        {
            Controls.Add(UiKit.MakeLabel(text, 9F, FontStyle.Bold,
                                         UiKit.TextMain, Pad, y));
            return y + 26;
        }

        private CheckBox AddCheck(string text, bool value, int y)
        {
            CheckBox c = UiKit.MakeCheck(text, value, Pad, y);
            Controls.Add(c);
            return c;
        }

        // --------------------------------------------------------------------
        //  提交
        // --------------------------------------------------------------------
        private KeepAwakeOptions ReadPanel()
        {
            KeepAwakeOptions o = new KeepAwakeOptions();
            o.PreventSleep = _chkSleep.Checked;
            o.PreventDisplay = _chkDisplay.Checked;
            o.DisableScreenSaver = _chkSaver.Checked;
            o.SimMouse = _chkSimMouse.Checked;
            o.SimKeyboard = _chkSimKey.Checked;
            o.OnlyWhenIdle = _chkOnlyIdle.Checked;
            o.IntervalSeconds = (int)_numInterval.Value;
            o.NudgeVirtualKey = _initial.NudgeVirtualKey;
            return o;
        }

        private bool Dirty()
        {
            KeepAwakeOptions now = ReadPanel();
            return now.PreventSleep != _initial.PreventSleep
                || now.PreventDisplay != _initial.PreventDisplay
                || now.DisableScreenSaver != _initial.DisableScreenSaver
                || now.SimMouse != _initial.SimMouse
                || now.SimKeyboard != _initial.SimKeyboard
                || now.OnlyWhenIdle != _initial.OnlyWhenIdle
                || now.IntervalSeconds != _initial.IntervalSeconds;
        }

        private void Apply()
        {
            _tray.CommitFromPanel(ReadPanel());
            _initial.CopyFrom(_tray.CurrentOptions);
            RefreshState();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();

            // 直接关窗等同于「应用」：以前这里只 Save 而不同步菜单、不重启引擎，
            // 结果磁盘是新值、菜单和引擎还是旧值。
            if (!_applied && Dirty())
                Apply();
        }

        // --------------------------------------------------------------------
        //  状态显示
        // --------------------------------------------------------------------
        private void RefreshState()
        {
            if (_engine.Running)
            {
                _lblState.Text = string.Format("守护中 · 已运行 {0} · 已模拟 {1} 次",
                    UiKit.FormatDuration(_engine.RunningFor),
                    _engine.NudgeCount);

                string err = _engine.LastError;
                _lblState.ForeColor = string.IsNullOrEmpty(err) ? UiKit.OkGreen
                                                               : UiKit.WarnRed;
            }
            else
            {
                _lblState.Text = "未运行（请从托盘菜单点击「开始守护」）";
                _lblState.ForeColor = UiKit.IdleGrey;
            }
        }
    }
}