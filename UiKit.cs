// ============================================================================
//  StayAwake - 界面公共件
//
//  两个窗体（设置面板 / 图形界面）原本各写一套字体与控件工厂，
//  复制粘贴之下漏传 Location 就把三个分组标题全叠到了 (0,0)。
//  这里把工厂收敛到一处，并且让坐标成为**必填参数** —— 编译期挡住同类错误。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace StayAwake
{
    internal static class UiKit
    {
        // ---- 配色 ----
        internal static readonly Color CardBg     = Color.FromArgb(0xF7, 0xF8, 0xFA);
        internal static readonly Color CardBorder = Color.FromArgb(0xE3, 0xE6, 0xEB);
        internal static readonly Color TextMain   = Color.FromArgb(0x1F, 0x23, 0x28);
        internal static readonly Color TextSub    = Color.FromArgb(0x6B, 0x72, 0x80);
        internal static readonly Color TextFaint  = Color.FromArgb(0xA0, 0xA6, 0xAE);
        internal static readonly Color Accent     = Color.FromArgb(0x2F, 0x6F, 0xED);
        internal static readonly Color OkGreen    = Color.FromArgb(0x16, 0x9B, 0x62);
        internal static readonly Color IdleGrey   = Color.FromArgb(0x8A, 0x90, 0x99);
        internal static readonly Color WarnRed    = Color.FromArgb(0xC0, 0x39, 0x2B);

        // ---- 字体（进程内共享一份，永不释放）----
        // 原先每个窗体 new Font(...) 赋给 Font 属性，而 Control 不接管所有权，
        // 反复开关设置面板就会不断累积 GDI 字体句柄。
        //
        // MakeLabel 更早漏了一处：它每次调用都 new Font(...)，且字号五花八门
        // （8/9/10/11/13/15F），没法直接复用上面三个静态字体。实测 Font 有终结器、
        // 不会真的泄漏 GDI 句柄，但每次 new Font 都要走一遍字体名解析
        // （GdipCreateFontFamilyFromName），一次开关面板要做十几次。
        // 统一收进 FontCache：每种 (字号, 字形) 全进程只有一个实例。
        private static readonly Dictionary<string, Font> FontCache =
            new Dictionary<string, Font>(StringComparer.Ordinal);

        internal static readonly Font FontUi     = MakeFont(9F, FontStyle.Regular);
        internal static readonly Font FontUiBold = MakeFont(9F, FontStyle.Bold);
        internal static readonly Font FontButton = MakeFont(11F, FontStyle.Bold);

        /// <summary>
        /// 取共享字体。返回值归本类所有，调用方（Label / Form）**不要**释放 ——
        /// Control 不接管 Font 的所有权，提前 Dispose 会让所有引用它的控件炸掉。
        /// </summary>
        private static Font MakeFont(float size, FontStyle style)
        {
            string key = size.ToString("R", CultureInfo.InvariantCulture)
                         + "/" + (int)style;

            lock (FontCache)
            {
                Font cached;
                if (FontCache.TryGetValue(key, out cached)) return cached;

                Font created = CreateFont(size, style);
                FontCache[key] = created;
                return created;
            }
        }

        private static Font CreateFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, style);
            }
            catch (Exception)
            {
                // 系统没装雅黑（精简版 Windows / Server Core）时退回默认字体
                return new Font(SystemFonts.DefaultFont.FontFamily, size, style);
            }
        }

        // ====================================================================
        //  控件工厂
        // ====================================================================

        /// <summary>
        /// 文本标签。x / y 是必填参数 —— 没有默认值就是为了让「忘了定位」
        /// 这种编译能过、运行时把控件叠在一起的错误根本写不出来。
        /// </summary>
        internal static Label MakeLabel(string text, float size, FontStyle style,
                                        Color color, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = MakeFont(size, style);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        /// <summary>固定宽高的文本（需要居中对齐 / 换行时用）。</summary>
        internal static Label MakeLabelBoxed(string text, float size, FontStyle style,
                                             Color color, int x, int y, int w, int h)
        {
            Label l = MakeLabel(text, size, style, color, x, y);
            l.AutoSize = false;
            l.Size = new Size(w, h);
            return l;
        }

        internal static CheckBox MakeCheck(string text, bool value, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.Location = new Point(x, y);
            c.AutoSize = true;
            c.BackColor = Color.Transparent;
            c.ForeColor = TextMain;
            return c;
        }

        internal static Panel MakeCard(int x, int y, int w, int h)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(w, h);
            p.BackColor = CardBg;
            p.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(CardBorder))
                using (GraphicsPath gp = RoundedRect(new Rectangle(0, 0, w - 1, h - 1), 8))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawPath(pen, gp);
                }
            };
            return p;
        }

        internal static Control MakeSep(int x, int y, int w)
        {
            Panel l = new Panel { Location = new Point(x, y), Size = new Size(w, 1) };
            l.BackColor = CardBorder;
            return l;
        }

        internal static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath gp = new GraphicsPath();
            int d = radius * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        internal static Button MakeButton(string text, int x, int y, int w, int h,
                                          Color back, Color fore, bool bordered)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = fore;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            b.FlatAppearance.BorderSize = bordered ? 1 : 0;
            if (bordered) b.FlatAppearance.BorderColor = CardBorder;
            return b;
        }

        /// <summary>
        /// 状态圆点。
        ///
        /// 必须是 UserPaint 控件：Label / Panel 默认带 ControlStyles.Opaque，
        /// WinForms 走原生绘制，**Paint 事件根本不会触发**，挂上去的 Paint
        /// 处理函数一次都不会跑（只会画出一个方块）。
        /// </summary>
        internal sealed class DotIndicator : Control
        {
            private Color _dotColor = IdleGrey;

            internal DotIndicator()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                         | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Opaque, false);
                // 不加这个样式，设 Color.Transparent 会抛
                // ArgumentException「控件不支持透明背景色」
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                TabStop = false;
            }

            internal Color DotColor
            {
                get { return _dotColor; }
                set { _dotColor = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Parent != null ? Parent.BackColor : Color.White);
                using (SolidBrush b = new SolidBrush(_dotColor))
                    e.Graphics.FillEllipse(b, 0, 0, Width, Height);
            }
        }

        internal static DotIndicator MakeStatusDot(int x, int y, int size)
        {
            DotIndicator dot = new DotIndicator();
            dot.Location = new Point(x, y);
            dot.Size = new Size(size, size);
            return dot;
        }

        // ====================================================================
        //  格式化
        // ====================================================================

        /// <summary>"1 小时 02 分 03 秒" / "02 分 03 秒" —— 带小时，别让长跑显示成 5 分。</summary>
        internal static string FormatDuration(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            long totalHours = (long)ts.TotalHours;

            if (totalHours > 0)
                return string.Format("{0} 小时 {1:00} 分 {2:00} 秒",
                                     totalHours, ts.Minutes, ts.Seconds);

            return string.Format("{0:00} 分 {1:00} 秒", ts.Minutes, ts.Seconds);
        }

        internal static string FormatSeconds(long ms)
        {
            if (ms < 0) return "未知";
            return (ms / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}