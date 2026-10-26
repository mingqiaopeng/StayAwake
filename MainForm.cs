// ============================================================================
//  StayAwake - 图形界面主窗口（--gui）
// ============================================================================

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace StayAwake
{
    internal sealed class MainForm : Form
    {
        private readonly KeepAwakeEngine _engine = new KeepAwakeEngine();
        private readonly KeepAwakeOptions _opt = new KeepAwakeOptions();

        private UiKit.DotIndicator _lblDot;
        private Label _lblState;
        private Label _lblStateDetail;
        private Label _lblNext;
        private Label _lblStats;

        private CheckBox _chkSleep;
        private CheckBox _chkDisplay;
        private CheckBox _chkDisableSaver;
        private CheckBox _chkSimMouse;
        private CheckBox _chkSimKey;
        private CheckBox _chkOnlyIdle;
        private CheckBox _chkAutoStart;
        private NumericUpDown _numInterval;
        private Button _btnToggle;

        private System.Windows.Forms.Timer _timer;
        private DateTime _nextPaint;

        /// <summary>开机自启项的重入闸门，理由见 TrayController 同名字段。</summary>
        private bool _inAutoStartUpdate;

        public MainForm()
        {
            _opt.CopyFrom(Settings.Current);

            BuildUi();
            BuildEvents();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTimerTick;
            _timer.Start();

            UpdateUiState();
        }

        // --------------------------------------------------------------------
        private void BuildUi()
        {
            Text = "StayAwake — 防止自动锁屏与休眠";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(480, 620);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(0xF2, 0xF4, 0xF7);
            Font = UiKit.FontUi;
            Icon = TrayIconFactory.CreateAppIcon(32);

            // ---- 顶栏 ----
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = Color.White
            };
            header.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UiKit.CardBorder))
                    e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            header.Controls.Add(UiKit.MakeLabelBoxed("StayAwake", 15F, FontStyle.Bold,
                                                      UiKit.TextMain, 20, 14, 300, 26));
            header.Controls.Add(UiKit.MakeLabelBoxed("定时模拟输入，避免自动锁屏与休眠",
                                                      9F, FontStyle.Regular,
                                                      UiKit.TextSub, 21, 42, 340, 20));

            _lblDot = UiKit.MakeStatusDot(430, 22, 12);
            header.Controls.Add(_lblDot);

            Controls.Add(header);

            // ---- 状态卡 ----
            Panel statusCard = UiKit.MakeCard(20, 96, 440, 78);
            _lblState = UiKit.MakeLabelBoxed("未运行", 13F, FontStyle.Bold,
                                              UiKit.IdleGrey, 18, 16, 300, 26);
            _lblStateDetail = UiKit.MakeLabelBoxed("点击下方按钮开始守护", 9F,
                                                    FontStyle.Regular, UiKit.TextSub,
                                                    19, 45, 404, 20);
            statusCard.Controls.Add(_lblState);
            statusCard.Controls.Add(_lblStateDetail);
            Controls.Add(statusCard);

            // ---- 选项卡 ----
            Panel opt = UiKit.MakeCard(20, 186, 440, 320);

            // 每一个分组标题都当场 Add 进容器。
            // 原先三个 MakeLabel 都算好了 y 却没传、也没 Add，
            // 于是全落在 (0,0) 叠成一摞，只剩最后添加的那个可见。
            const int indent = 44;
            int y = 16;
            opt.Controls.Add(UiKit.MakeLabel("保持唤醒", 10F, FontStyle.Bold,
                                             UiKit.TextMain, indent, y));
            y += 26;

            _chkSleep = UiKit.MakeCheck("阻止系统进入睡眠", _opt.PreventSleep, indent, y);
            _chkDisplay = UiKit.MakeCheck("阻止显示器关闭", _opt.PreventDisplay, indent, y + 26);
            _chkDisableSaver = UiKit.MakeCheck("禁用屏幕保护程序（退出时恢复原状态）",
                                               _opt.DisableScreenSaver, indent, y + 52);
            opt.Controls.Add(_chkSleep);
            opt.Controls.Add(_chkDisplay);
            opt.Controls.Add(_chkDisableSaver);
            y += 84;
            opt.Controls.Add(UiKit.MakeSep(indent, y - 10, 392));

            opt.Controls.Add(UiKit.MakeLabel("模拟用户活动（防锁屏）", 10F, FontStyle.Bold,
                                             UiKit.TextMain, indent, y));
            y += 26;

            _chkSimMouse = UiKit.MakeCheck("发送鼠标移动消息（1px 往返）",
                                           _opt.SimMouse, indent, y);
            _chkSimKey = UiKit.MakeCheck("发送键盘消息（F24，无副作用）",
                                         _opt.SimKeyboard, indent, y + 26);
            _chkOnlyIdle = UiKit.MakeCheck("仅在系统空闲时模拟（推荐）",
                                           _opt.OnlyWhenIdle, indent, y + 52);
            opt.Controls.Add(_chkSimMouse);
            opt.Controls.Add(_chkSimKey);
            opt.Controls.Add(_chkOnlyIdle);
            y += 84;
            opt.Controls.Add(UiKit.MakeSep(indent, y - 10, 392));

            opt.Controls.Add(UiKit.MakeLabel("其他", 10F, FontStyle.Bold,
                                             UiKit.TextMain, indent, y));
            y += 26;

            _chkAutoStart = UiKit.MakeCheck("开机自动启动（托盘常驻）",
                                            AutoStart.IsEnabled(), indent, y);
            opt.Controls.Add(_chkAutoStart);
            y += 34;
            opt.Controls.Add(UiKit.MakeSep(indent, y - 12, 392));

            opt.Controls.Add(UiKit.MakeLabel("间隔", 9F, FontStyle.Regular,
                                             UiKit.TextMain, indent, y + 6));
            _numInterval = new NumericUpDown
            {
                Location = new Point(indent + 42, y + 2),
                Width = 66,
                Minimum = 5,
                Maximum = 3600,
                Value = KeepAwakeOptions.ClampInterval(_opt.IntervalSeconds)
            };
            opt.Controls.Add(_numInterval);
            opt.Controls.Add(UiKit.MakeLabelBoxed("秒（5 ~ 3600）", 9F, FontStyle.Regular,
                                                  UiKit.TextSub, indent + 116, y + 6,
                                                  180, 20));

            Controls.Add(opt);

            // ---- 底部 ----
            _btnToggle = UiKit.MakeButton("开始守护", 20, 518, 440, 46,
                                          UiKit.Accent, Color.White, false);
            _btnToggle.Font = UiKit.FontButton;
            _btnToggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x25, 0x5F, 0xD0);
            _btnToggle.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x1E, 0x50, 0xB0);

            _lblNext = UiKit.MakeLabelBoxed("", 9F, FontStyle.Regular, UiKit.TextSub,
                                            20, 570, 440, 20);
            _lblNext.TextAlign = ContentAlignment.MiddleCenter;

            _lblStats = UiKit.MakeLabelBoxed("", 8F, FontStyle.Regular, UiKit.TextFaint,
                                             20, 592, 440, 18);
            _lblStats.TextAlign = ContentAlignment.MiddleCenter;

            Controls.Add(_btnToggle);
            Controls.Add(_lblNext);
            Controls.Add(_lblStats);
        }

        private void BuildEvents()
        {
            _btnToggle.Click += OnToggleClick;
            FormClosing += OnFormClosing;
            _chkAutoStart.CheckedChanged += (s, e) => OnAutoStartChanged();

            foreach (CheckBox c in new[] { _chkSleep, _chkDisplay, _chkDisableSaver,
                                           _chkSimMouse, _chkSimKey, _chkOnlyIdle })
            {
                // 勾选框一次点击 = 一次落盘，量很小，直接写。
                c.CheckedChanged += (s, e) => { ApplyOptionsToEngine(); SaveOptions(); };
            }

            // 数值框不一样：输入 "300" 会依次触发 3 → 30 → 300 三个 ValueChanged。
            // 每次都落盘就是三次「写临时文件 + 原子替换」，纯属浪费 ——
            // 而且中途那几个值（3 秒、30 秒）根本不是用户想要的结果。
            //
            // 拆成两步：引擎立刻生效（用户要看到倒计时马上变），
            // 落盘推迟到离开输入框时统一做一次。
            _numInterval.ValueChanged += (s, e) => ApplyOptionsToEngine();
            _numInterval.Validated += (s, e) => SaveOptions();
        }

        /// <summary>
        /// 把界面上的勾选收集成配置，让引擎立即生效。
        ///
        /// 这一步**不落盘** —— 调用方决定要不要写文件。
        /// </summary>
        private void ApplyOptionsToEngine()
        {
            _opt.PreventSleep = _chkSleep.Checked;
            _opt.PreventDisplay = _chkDisplay.Checked;
            _opt.DisableScreenSaver = _chkDisableSaver.Checked;
            _opt.SimMouse = _chkSimMouse.Checked;
            _opt.SimKeyboard = _chkSimKey.Checked;
            _opt.OnlyWhenIdle = _chkOnlyIdle.Checked;
            _opt.IntervalSeconds = (int)_numInterval.Value;

            if (_engine.Running)
                _engine.Reconfigure(_opt);

            UpdateUiState();
        }

        /// <summary>把当前配置写盘。原子替换，不会写出半截文件。</summary>
        private void SaveOptions()
        {
            Settings.Save(_opt);
        }

        // --------------------------------------------------------------------
        private void OnAutoStartChanged()
        {
            if (_inAutoStartUpdate)
                return;

            bool want = _chkAutoStart.Checked;

            if (AutoStart.Set(want))
            {
                if (want)
                {
                    MessageBox.Show("已添加开机自启。\n\n下次开机将以托盘常驻方式启动（无窗口）。",
                        "StayAwake", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                string why = string.IsNullOrEmpty(AutoStart.LastError)
                    ? "未知原因" : AutoStart.LastError;

                // 回滚时不能再触发一次处理逻辑，否则注册表整体不可写时会无限递归。
                _inAutoStartUpdate = true;
                _chkAutoStart.Checked = AutoStart.Cached().Registered;
                _inAutoStartUpdate = false;

                Log.Warn("开机自启开关失败，已回滚勾选: " + why);
                MessageBox.Show("修改启动项失败：\n" + why,
                    "StayAwake", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            UpdateUiState();
        }

        // --------------------------------------------------------------------
        private void OnToggleClick(object sender, EventArgs e)
        {
            ApplyOptionsToEngine();
            // 显式的用户动作，此刻把配置落盘是稳妥的。
            SaveOptions();

            if (_engine.Running)
            {
                _engine.Stop();
            }
            else
            {
                _engine.Start(_opt);
            }
            UpdateUiState();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_engine.Running)
            {
                _engine.Tick();

                if (DateTime.Now >= _nextPaint)
                {
                    _nextPaint = DateTime.Now.AddMilliseconds(400);
                    UpdateUiState();
                }
            }
            else if (DateTime.Now >= _nextPaint)
            {
                _nextPaint = DateTime.Now.AddMilliseconds(400);
                UpdateUiState();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _engine.Stop();
            SaveOptions();
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
        }

        // --------------------------------------------------------------------
        private void UpdateUiState()
        {
            UpdateToggleButton();

            AutoStartInfo autostart = AutoStart.Cached();
            UpdateAutoStartHint(autostart);

            if (!_engine.Running)
            {
                _lblDot.DotColor = UiKit.IdleGrey;
                _lblState.Text = "未运行";
                _lblState.ForeColor = UiKit.IdleGrey;
                _lblStateDetail.ForeColor = UiKit.TextSub;   // 必须复位，否则报错后停止会一直留红
                _lblStateDetail.Text = "点击下方按钮开始守护";
                _lblNext.Text = "";
                return;
            }

            _lblDot.DotColor = UiKit.OkGreen;
            _lblState.Text = "守护中";
            _lblState.ForeColor = UiKit.OkGreen;

            long idleMs = KeepAwakeEngine.GetIdleMilliseconds();

            string err = _engine.LastError;
            if (!string.IsNullOrEmpty(err))
            {
                _lblStateDetail.ForeColor = UiKit.WarnRed;
                _lblStateDetail.Text = err;
            }
            else
            {
                _lblStateDetail.ForeColor = UiKit.TextSub;
                _lblStateDetail.Text = string.Format(
                    "已运行 {0} · 系统已空闲 {1} 秒",
                    UiKit.FormatDuration(_engine.RunningFor),
                    UiKit.FormatSeconds(idleMs));
            }

            _lblNext.Text = string.Format(
                "下次动作 {0} 秒后 · 已模拟 {1} 次 · 空闲跳过 {2} 次 · 失败 {3} 次",
                _engine.SecondsToNextAction, _engine.NudgeCount,
                _engine.IdleSkipCount, _engine.NudgeFailCount);
        }

        private void UpdateToggleButton()
        {
            _btnToggle.Text = _engine.Running ? "停止守护" : "开始守护";
            _btnToggle.BackColor = _engine.Running
                ? Color.FromArgb(0xDC, 0x26, 0x26)
                : UiKit.Accent;
        }

        private void UpdateAutoStartHint(AutoStartInfo autostart)
        {
            if (autostart.State == AutoStartState.Foreign)
                _lblStats.Text = "开机自启指向其它路径：" + autostart.Command;
            else if (autostart.State == AutoStartState.Current)
                _lblStats.Text = "已配置开机自启（托盘常驻）";
            else
                _lblStats.Text = "";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            base.Dispose(disposing);
        }
    }
}