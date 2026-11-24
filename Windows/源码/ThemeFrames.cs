using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TubeGet
{
    // Cache native bitmap surfaces so animation frames only copy and blend visible pixels.
    internal sealed class ThemeFrames : IDisposable
    {
        private readonly Surface before, after, blurred;
        private readonly Size size;

        internal ThemeFrames(Bitmap oldFrame, Bitmap newFrame, Bitmap softFrame)
        {
            size = oldFrame.Size;
            try
            {
                before = new Surface(oldFrame);
                after = new Surface(newFrame);
                blurred = new Surface(softFrame);
            }
            catch { Dispose(); throw; }
        }

        internal void Draw(Graphics graphics, Point center, float radius, float progress)
        {
            progress = Math.Max(0, Math.Min(1, progress));
            IntPtr target = graphics.GetHdc();
            int saved = SaveDC(target);
            IntPtr circle = IntPtr.Zero;
            try
            {
                Copy(target, progress == 1 ? after : before);
                if (progress <= 0 || progress >= 1) return;
                float remaining = 1 - progress;
                float r = radius * (1 - remaining * remaining * remaining);
                circle = CreateEllipticRgn((int)Math.Floor(center.X - r), (int)Math.Floor(center.Y - r),
                    (int)Math.Ceiling(center.X + r), (int)Math.Ceiling(center.Y + r));
                if (circle == IntPtr.Zero || ExtSelectClipRgn(target, circle, 1) == 0)
                    throw new ExternalException("无法创建主题过渡区域");
                Copy(target, after);
                // Keep the revealed content fully soft while the circle becomes visible.
                // The previous curve had already exposed mostly sharp text at mid-transition.
                float clarity = Math.Max(0, (progress - 0.65f) / 0.35f);
                clarity = clarity * clarity * (3 - 2 * clarity);
                var blend = new BlendFunction { SourceConstantAlpha = (byte)(255 * (1 - clarity)) };
                if (blend.SourceConstantAlpha > 8 && !AlphaBlend(target, 0, 0, size.Width, size.Height,
                    blurred.Dc, 0, 0, size.Width, size.Height, blend))
                    throw new ExternalException("无法合成主题过渡");
            }
            finally
            {
                if (saved != 0) RestoreDC(target, saved);
                if (circle != IntPtr.Zero) DeleteObject(circle);
                graphics.ReleaseHdc(target);
            }
        }

        private void Copy(IntPtr target, Surface source)
        {
            if (!BitBlt(target, 0, 0, size.Width, size.Height, source.Dc, 0, 0, 0x00CC0020))
                throw new ExternalException("无法绘制主题过渡");
        }

        public void Dispose()
        {
            if (before != null) before.Dispose();
            if (after != null) after.Dispose();
            if (blurred != null) blurred.Dispose();
        }

        private sealed class Surface : IDisposable
        {
            internal IntPtr Dc;
            private IntPtr bitmap, previous;

            internal Surface(Bitmap image)
            {
                try
                {
                    bitmap = image.GetHbitmap(Color.Black);
                    Dc = CreateCompatibleDC(IntPtr.Zero);
                    if (Dc == IntPtr.Zero) throw new ExternalException("无法创建主题图像");
                    previous = SelectObject(Dc, bitmap);
                    if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                        throw new ExternalException("无法加载主题图像");
                }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                if (Dc != IntPtr.Zero)
                {
                    if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(Dc, previous);
                    DeleteDC(Dc);
                    Dc = IntPtr.Zero;
                }
                if (bitmap != IntPtr.Zero) { DeleteObject(bitmap); bitmap = IntPtr.Zero; }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction { internal byte Operation, Flags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern int SaveDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool RestoreDC(IntPtr dc, int saved);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern int ExtSelectClipRgn(IntPtr dc, IntPtr region, int mode);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height,
            IntPtr source, int sx, int sy, uint operation);
        [DllImport("msimg32.dll")] private static extern bool AlphaBlend(IntPtr target, int x, int y, int width, int height,
            IntPtr source, int sx, int sy, int swidth, int sheight, BlendFunction blend);
    }
}
