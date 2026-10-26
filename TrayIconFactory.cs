// ============================================================================
//  StayAwake - 托盘图标生成
//
//  图标来源：用户提供的 icons8 verified_account 扁平线性图标（5 种颜色），
//  按状态着色。做法是「裁透明边 + 4x 超采样降采样」，保证托盘 16px 下依然清晰。
//
//  铁律：本文件的所有对外方法都不允许抛异常。
//  资源缺失、目录不可写、磁盘满 —— 任何一种都不该让整个程序启动不了。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace StayAwake
{
    /// <summary>托盘状态，决定图标配色。</summary>
    internal enum TrayState
    {
        /// <summary>未运行 / 已停止 —— 灰色</summary>
        Idle = 0,

        /// <summary>守护中 —— 蓝色（激活色）</summary>
        Running = 1,

        /// <summary>警告 / 异常（如电源请求失败）—— 红色</summary>
        Warning = 2
    }

    internal static class TrayIconFactory
    {
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSMICON = 49;

        /// <summary>缓存版本戳：资源或渲染算法变化时自动重建磁盘缓存。</summary>
        private const string CacheTag = "v4";

        // 嵌入资源中的图标名（编译时由 /resource 注入）
        //
        // 三个图标对应三个可达状态；NAVY 不用于托盘，它是窗口 / 任务栏图标
        // （见 CreateAppIcon）。Grey 是默认分支，所以三个状态都有图可用。
        private const string RES_GREY  = "SA_ICON_GREY.png";
        private const string RES_RED   = "SA_ICON_RED.png";
        private const string RES_LIGHT = "SA_ICON_LIGHT.png";
        private const string RES_NAVY  = "SA_ICON_NAVY.png";

        private static string _cacheDir;
        private static Icon _appIcon;
        private static int _appIconSize;
        private static string _tagWriteWarned;

        /// <summary>
        /// 裁透明边后留下的呼吸位比例。
        ///
        /// 主体占画布 96%，四周各留 2%。贴着边的图标看起来拥挤，
        /// 而托盘图标的实际绘制区域还要再被系统缩一圈。
        /// </summary>
        private const double BreathRatio = 0.96;

        /// <summary>
        /// 裁透明边时的 alpha 阈值。
        ///
        /// 大于这个值才算「有内容」。取 10 而不是 0，是为了忽略
        /// 抗锯齿边缘残下的极淡像素 —— 它们会把包围盒撑大，
        /// 主体反而显得偏小。
        /// </summary>
        private const int InkAlphaThreshold = 10;

        private static string CacheDir
        {
            get
            {
                if (_cacheDir != null) return _cacheDir;
                try
                {
                    string d = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "StayAwake");
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    _cacheDir = d;
                }
                catch (Exception ex)
                {
                    Log.Error("图标缓存目录不可用", ex);
                    _cacheDir = null;
                }
                return _cacheDir;
            }
        }

        private static int TraySize
        {
            get
            {
                try
                {
                    int cx = GetSystemMetrics(SM_CXSMICON);
                    return (cx > 0 && cx <= 64) ? cx : 16;
                }
                catch
                {
                    return 16;
                }
            }
        }

        // ====================================================================
        //  对外接口
        // ====================================================================

        /// <summary>
        /// 按状态获取托盘图标。返回的实例归调用方所有（用完请 Dispose），
        /// 但极端情况下可能返回 null —— 调用方必须能处理。
        /// </summary>
        internal static Icon CreateTrayIcon(TrayState state)
        {
            Icon icon = null;

            try
            {
                string path = BuildIco(state);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    icon = new Icon(path, new Size(TraySize, TraySize));
            }
            catch (Exception ex)
            {
                Log.Error("生成托盘图标失败（状态 " + (int)state + "）", ex);
                if (icon != null) { try { icon.Dispose(); } catch { } icon = null; }
            }

            if (icon == null)
            {
                try
                {
                    string alt = BuildSingleIco(ResFor(state), 32, "fallback_" + (int)state);
                    if (!string.IsNullOrEmpty(alt) && File.Exists(alt))
                        icon = new Icon(alt, new Size(32, 32));
                }
                catch (Exception ex)
                {
                    Log.Error("生成托盘图标兜底方案失败", ex);
                }
            }

            if (icon == null)
            {
                try { icon = (Icon)SystemIcons.Application.Clone(); }
                catch { icon = null; }
            }

            return icon;
        }

        /// <summary>
        /// 窗口 / 任务栏图标。
        ///
        /// 刻意返回**同一个共享实例**：Form 不会释放赋给 Icon 属性的对象，
        /// 而 new Icon(path,…) 每次都开一个原生句柄。设置面板可以反复开关，
        /// 按老写法每开一次就漏一个句柄。
        /// 共享实例由本类持有，进程生命周期内有效，调用方**不要**释放。
        /// </summary>
        internal static Icon CreateAppIcon(int size)
        {
            if (size <= 0) size = 32;
            if (_appIcon != null && _appIconSize == size) return _appIcon;

            Icon icon = null;
            try
            {
                string path = BuildSingleIco(RES_NAVY, size, "app_" + size);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    icon = new Icon(path, new Size(size, size));
            }
            catch (Exception ex)
            {
                Log.Error("生成窗口图标失败", ex);
            }

            if (icon == null)
            {
                try { icon = (Icon)SystemIcons.Application.Clone(); }
                catch { icon = null; }
            }

            if (_appIcon != null)
            {
                try { _appIcon.Dispose(); } catch { }
            }
            _appIcon = icon;
            _appIconSize = size;

            return icon;
        }

        private static string ResFor(TrayState state)
        {
            switch (state)
            {
                case TrayState.Running: return RES_LIGHT;
                case TrayState.Warning: return RES_RED;
                default:                return RES_GREY;
            }
        }

        // ====================================================================
        //  ICO 构建
        // ====================================================================

        private static string BuildIco(TrayState state)
        {
            string dir = CacheDir;
            if (string.IsNullOrEmpty(dir)) return null;

            string path = Path.Combine(dir, "tray_" + (int)state + ".ico");
            string res = ResFor(state);
            string tag = res + "_" + CacheTag;
            string tagFile = path + ".tag";

            if (IsCached(path, tagFile, tag))
                return path;

            using (Image src = LoadResource(res))
            {
                // 墨迹包围盒只算一次，供全部 7 个尺寸复用
                // （老代码每个尺寸都重扫一遍 200×200 = 28 万像素）
                Rectangle ink = GetInkBounds(src);

                int[] sizes = { 16, 20, 24, 32, 48, 64, 256 };
                byte[][] pngs = new byte[sizes.Length][];

                for (int i = 0; i < sizes.Length; i++)
                {
                    using (Bitmap b = RenderAt(src, ink, sizes[i]))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        b.Save(ms, ImageFormat.Png);
                        pngs[i] = ms.ToArray();
                    }
                }

                WriteIco(sizes, pngs, path);
            }

            WriteTag(tagFile, tag);
            Log.Debug("已生成托盘 ICO: " + path);
            return path;
        }

        /// <summary>
        /// 生成单尺寸 ICO 并缓存到磁盘。
        /// 窗口图标不能用 Bitmap.GetHicon() 那条路：
        /// Icon.Clone() 会接管原生句柄，而外层 using 又会立刻 Dispose 掉返回对象，
        /// 导致 Form.Show() 时 Icon.get_Handle 抛 ObjectDisposedException。
        /// 统一走「渲染 → 写 ico 文件 → new Icon(path)」，句柄所有权交给 .NET 自身管理。
        /// </summary>
        private static string BuildSingleIco(string res, int size, string cacheName)
        {
            string dir = CacheDir;
            if (string.IsNullOrEmpty(dir)) return null;

            string path = Path.Combine(dir, cacheName + "_" + size + ".ico");
            string tagFile = path + ".tag";
            string tag = res + "_" + CacheTag + "_" + size;

            if (IsCached(path, tagFile, tag))
                return path;

            try
            {
                using (Image src = LoadResource(res))
                {
                    Rectangle ink = GetInkBounds(src);
                    using (Bitmap b = RenderAt(src, ink, size))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        b.Save(ms, ImageFormat.Png);
                        WriteIco(new int[] { size }, new byte[][] { ms.ToArray() }, path);
                    }
                }

                WriteTag(tagFile, tag);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("生成单尺寸 ICO 失败: " + cacheName, ex);
                return null;
            }
        }

        private static bool IsCached(string path, string tagFile, string tag)
        {
            try
            {
                if (!File.Exists(path)) return false;

                FileInfo fi = new FileInfo(path);
                if (fi.Length <= 0) return false;

                // 版本戳读得到 → 精确比对，CacheTag 一改就必然重建。
                if (File.Exists(tagFile))
                {
                    string onDisk = File.ReadAllText(tagFile);
                    // 内容对不上 = 确实过期（算法/资源变了），必须重建。
                    return string.Equals(onDisk, tag, StringComparison.Ordinal);
                }

                // 版本戳缺失或读不出来 —— 受控文件夹访问、部分杀软只放行 .ico、
                // 磁盘清理工具顺手删掉了缓存目录，都会走到这里。
                // 这种情况下若每次都重建，光一次状态跳变就要在 UI 线程上烧掉
                // 实测 109~141ms（7 个尺寸，含 984×984 的 4× 超采样位图）。
                // 托盘图标在 Running / Warning 之间抖动时更是每秒一次。
                // 退一步用 exe 的修改时间当下限：程序没重编译过就不重建。
                return SourceStampUtc == DateTime.MinValue
                       || fi.LastWriteTimeUtc >= SourceStampUtc;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 当前程序集（= 本 exe）的最后修改时间，作为图标缓存的兜底下限。
        /// 取不到时返回 MinValue，IsCached 里会把它当成「无法判定，一律信任现有缓存」。
        /// </summary>
        private static readonly DateTime SourceStampUtc = ResolveSourceStampUtc();

        private static DateTime ResolveSourceStampUtc()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                    return File.GetLastWriteTimeUtc(exe);
            }
            catch (Exception ex)
            {
                Log.Warn("无法确定 exe 修改时间，图标缓存将只靠版本戳判定: " + ex.Message);
            }
            return DateTime.MinValue;
        }

        private static void WriteTag(string tagFile, string tag)
        {
            try { File.WriteAllText(tagFile, tag); }
            catch (Exception ex)
            {
                // 写不出去不是致命问题：IsCached 有 exe 时间戳兜底。
                // 但要降级成一次性警告，不能每建一次图标就刷一条。
                Log.Once(ref _tagWriteWarned,
                         "图标缓存版本戳写入失败，缓存将改用 exe 修改时间判定: "
                         + ex.Message);
            }
        }

        /// <summary>
        /// 渲染到目标尺寸：裁掉透明边 → 留 2% 呼吸位 → 4 倍超采样 → 降采样。
        /// </summary>
        private static Bitmap RenderAt(Image src, Rectangle ink, int size)
        {
            if (size <= 0) size = 16;

            // 1) 内区尺寸（2% 呼吸位）
            int inner = (int)Math.Round(size * BreathRatio);
            if (inner < 8) inner = size;
            if (inner > size) inner = size;

            if (inner == size)
                return RenderHi(src, ink, inner, size);

            // 2) 先渲染到内区，再居中放进目标画布
            Bitmap small = RenderHi(src, ink, inner, size);

            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                // 必须用浮点偏移：整数 (size-inner)/2 整除后永远是 0，
                // 16/20/24px 的图标会被顶到左上角，底下空一行 —— 恰恰是最需要
                // 像素精度的三个尺寸。
                float off = (size - inner) / 2.0f;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(small, new RectangleF(off, off, inner, inner));
            }
            small.Dispose();
            return result;
        }

        /// <summary>4× 超采样后降采样到 inner 像素。</summary>
        private static Bitmap RenderHi(Image src, Rectangle ink, int inner, int size)
        {
            const int ss = 4;                 // 超采样倍数
            int big = inner * ss;

            using (Bitmap hi = new Bitmap(big, big, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(hi))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, big, big),
                                ink, GraphicsUnit.Pixel);
                }

                Bitmap small = new Bitmap(inner, inner, PixelFormat.Format32bppArgb);
                using (Graphics gs = Graphics.FromImage(small))
                {
                    gs.Clear(Color.Transparent);
                    gs.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    gs.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gs.DrawImage(hi, new Rectangle(0, 0, inner, inner));
                }

                return small;
            }
        }

        /// <summary>
        /// 扫描非透明像素的最小包围盒。
        /// 用 LockBits 一次性取出像素缓冲，避免逐点 GetPixel（GDI+ 下约 28 万次调用极慢）。
        /// </summary>
        private static Rectangle GetInkBounds(Image img)
        {
            using (Bitmap srcBmp = new Bitmap(img))
            using (Bitmap bmp = new Bitmap(srcBmp.Width, srcBmp.Height,
                                            PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImageUnscaled(srcBmp, 0, 0);

                int w = bmp.Width, h = bmp.Height;
                int minX = w, minY = h, maxX = -1, maxY = -1;

                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                                             ImageLockMode.ReadOnly,
                                             PixelFormat.Format32bppArgb);
                try
                {
                    int stride = bd.Stride;

                    // LockBits 对新建的 Bitmap 一定给正 stride。
                    // 负 stride 时 Scan0 指向最后一行首部，正向 Marshal.Copy 会越界，
                    // 所以这里显式拒掉而不是算出一个错的包围盒。
                    if (stride < 0)
                        return new Rectangle(0, 0, w, h);

                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        int rowMin = -1, rowMax = -1;

                        for (int x = 0; x < w; x++)
                        {
                            // Format32bppArgb 为 BGRA 排列，A 在每像素第 3 字节
                            if (buf[row + x * 4 + 3] > InkAlphaThreshold)
                            {
                                if (rowMin < 0) rowMin = x;
                                rowMax = x;
                            }
                        }

                        if (rowMin >= 0)
                        {
                            if (rowMin < minX) minX = rowMin;
                            if (rowMax > maxX) maxX = rowMax;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }

                if (maxX < 0 || maxY < 0)
                    return new Rectangle(0, 0, w, h);

                return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            }
        }

        /// <summary>
        /// 写 ICO。注意：必须先算完所有偏移再顺序写，
        /// 不要「写占位再回填」，那样容易让偏移错位重叠。
        /// </summary>
        private static void WriteIco(int[] sizes, byte[][] pngs, string path)
        {
            const int EntrySize = 16;
            int count = sizes.Length;
            long dataStart = 6 + EntrySize * count;

            long[] offsets = new long[count];
            long cursor = dataStart;
            for (int i = 0; i < count; i++)
            {
                offsets[i] = cursor;
                cursor += pngs[i].Length;
            }

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)count);

                for (int i = 0; i < count; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((ushort)1);
                    w.Write((ushort)32);
                    w.Write(pngs[i].Length);
                    w.Write((uint)offsets[i]);
                }

                for (int i = 0; i < count; i++)
                    w.Write(pngs[i]);

                w.Flush();
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        // ====================================================================
        //  资源加载
        // ====================================================================

        /// <summary>从程序集嵌入资源加载图标。</summary>
        internal static Image LoadResource(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string full = "StayAwake." + name;

            using (Stream s = asm.GetManifestResourceStream(full))
            {
                if (s == null)
                {
                    // 兜底：按短名遍历匹配
                    string[] all = asm.GetManifestResourceNames();
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i].EndsWith(name, StringComparison.OrdinalIgnoreCase))
                        {
                            using (Stream s2 = asm.GetManifestResourceStream(all[i]))
                            {
                                return ReadBitmap(s2);
                            }
                        }
                    }
                    throw new FileNotFoundException("嵌入资源未找到: " + full);
                }

                return ReadBitmap(s);
            }
        }

        private static Bitmap ReadBitmap(Stream s)
        {
            using (s)
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                ms.Position = 0;
                using (Image tmp = Image.FromStream(ms))
                    return new Bitmap(tmp);
            }
        }
    }
}