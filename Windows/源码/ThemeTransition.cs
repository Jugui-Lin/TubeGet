using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TubeGet
{
    // A separate owned surface remains visible while native controls underneath repaint.
    internal sealed class ThemeTransition : FirstPaintWindow
    {
        private static ThemeTransition active;
        private readonly Bitmap before;
        private Bitmap after, blurredAfter;
        private ThemeFrames frames;
        private readonly Timer clock = new Timer { Interval = 15 };
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Stopwatch handoff = new Stopwatch();
        private readonly Form host;
        private readonly Point origin;
        private readonly float maximumRadius;
        private readonly Region outline;
        private bool finalFrame;
        private bool hostCovered;
        private const double Duration = 720;
        private const double HandoffDuration = 180;

        private ThemeTransition(Form owner, Bitmap oldFrame, Point center, Rectangle frameBounds)
        {
            host = owner;
            before = oldFrame;
            origin = new Point(Math.Max(0, Math.Min(oldFrame.Width, center.X)),
                Math.Max(0, Math.Min(oldFrame.Height, center.Y)));
            double dx = Math.Max(origin.X, oldFrame.Width - origin.X);
            double dy = Math.Max(origin.Y, oldFrame.Height - origin.Y);
            maximumRadius = (float)Math.Sqrt(dx * dx + dy * dy) + 2;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = frameBounds;
            outline = CreateOutline(owner, ClientSize);
            if (outline != null) Region = outline;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            AccessibleName = "正在切换外观";
            Material.ThemeWindow(this, disableTransitions: true);
            HandleCreated += (s, e) =>
            {
                Material.DisableWindowDecorations(this);
                Material.HideWindowBorder(this);
            };
            clock.Tick += Tick;
            owner.ClientSizeChanged += Interrupted;
            owner.VisibleChanged += Interrupted;
            owner.LocationChanged += Moved;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000080; // NOACTIVATE | TOOLWINDOW
                parameters.ClassStyle &= ~0x00020000; // CS_DROPSHADOW
                return parameters;
            }
        }

        internal static bool Covers(Form form)
        {
            return active != null && !active.IsDisposed && active.host == form;
        }

        internal static void Run(Form owner, Action change)
        {
            Run(owner, new Point(owner.ClientSize.Width / 2, owner.ClientSize.Height / 2), change);
        }

        internal static void Run(Form owner, Point center, Action change)
        {
            if (active != null) active.Dispose();
            if (!owner.Visible || owner.WindowState == FormWindowState.Minimized ||
                owner.ClientSize.Width < 1 || owner.ClientSize.Height < 1 ||
                !SystemInformation.IsMenuAnimationEnabled)
            {
                change();
                return;
            }

            ThemeTransition overlay = null;
            Bitmap oldFrame = null;
            bool changed = false;
            try
            {
                Rectangle frameBounds = FrameBounds(owner);
                center = owner.PointToScreen(center);
                center.Offset(-frameBounds.Left, -frameBounds.Top);
                oldFrame = Snapshot(owner, frameBounds);
                overlay = new ThemeTransition(owner, oldFrame, center, frameBounds);
                active = overlay;
                overlay.Show(owner);
                // Present the old image before changing any live control or caption color.
                overlay.Update();
                Present();
                var layeredHost = owner as FirstPaintWindow;
                if (overlay.outline != null && layeredHost != null)
                    overlay.hostCovered = layeredHost.SetTransitionOpacity(0);
                change();
                changed = true;
                owner.PerformLayout();
                overlay.after = Snapshot(owner, frameBounds);
                overlay.blurredAfter = Blur(overlay.after);
                overlay.frames = new ThemeFrames(oldFrame, overlay.after, overlay.blurredAfter);
                overlay.elapsed.Start();
                overlay.clock.Start();
            }
            catch (Exception error)
            {
                if (overlay != null) overlay.Dispose();
                else if (oldFrame != null) oldFrame.Dispose();
                if (!(error is ArgumentException || error is ExternalException || error is OutOfMemoryException)) throw;
                if (!changed) change();
                owner.Invalidate(true);
            }
        }

        internal static Rectangle FrameBounds(Form owner)
        {
            NativeRectangle frame;
            Rectangle bounds = owner.Bounds;
            if (DwmGetWindowAttribute(owner.Handle, 9, out frame, Marshal.SizeOf(typeof(NativeRectangle))) == 0)
            {
                var visible = Rectangle.Intersect(bounds, Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom));
                if (visible.Width > 0 && visible.Height > 0) bounds = visible;
            }
            if (owner.WindowState == FormWindowState.Normal)
            {
                // Layered windows can report the invisible resize margins as part of the DWM frame.
                int margin = Math.Max(0, owner.PointToScreen(Point.Empty).X - owner.Left - 1);
                bounds.Intersect(new Rectangle(owner.Left + margin, owner.Top,
                    Math.Max(1, owner.Width - margin * 2), Math.Max(1, owner.Height - margin)));
            }
            else if (owner.WindowState == FormWindowState.Maximized)
                bounds.Intersect(Screen.FromHandle(owner.Handle).WorkingArea);
            return bounds.Width > 0 && bounds.Height > 0 ? bounds : owner.Bounds;
        }

        private static Region CreateOutline(Form owner, Size size)
        {
            if (NativeBorderThickness(owner) == 0) return null;
            using (var graphics = owner.CreateGraphics())
            using (var shape = Material.Shape(new RectangleF(0, 0, size.Width, size.Height),
                8 * graphics.DpiX / 96f))
                return new Region(shape);
        }

        private static int NativeBorderThickness(Form owner)
        {
            if (owner.WindowState != FormWindowState.Normal) return 0;
            const int VisibleFrameBorderThickness = 37;
            int border;
            if (DwmGetWindowAttribute(owner.Handle, VisibleFrameBorderThickness, out border, sizeof(int)) != 0) return 0;
            return Math.Max(1, border);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle { internal int Left, Top, Right, Bottom; }
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out NativeRectangle value, int size);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        private static void Present()
        {
            // Wait for the compositor, not just WM_PAINT, before exposing a changed window.
            DwmFlush();
        }

        private static Bitmap Snapshot(Form owner, Rectangle frameBounds)
        {
            using (var whole = new Bitmap(owner.Width, owner.Height, PixelFormat.Format32bppPArgb))
            {
                bool captured;
                using (var graphics = Graphics.FromImage(whole))
                {
                    IntPtr dc = graphics.GetHdc();
                    try { captured = PrintWindow(owner.Handle, dc, 2); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                if (!captured) owner.DrawToBitmap(whole, new Rectangle(Point.Empty, whole.Size));
                frameBounds.Offset(-owner.Left, -owner.Top);
                frameBounds.Intersect(new Rectangle(Point.Empty, whole.Size));
                Bitmap snapshot = whole.Clone(frameBounds, PixelFormat.Format32bppPArgb);
                try
                {
                    int edge = NativeBorderThickness(owner);
                    if (edge > 0) FillSnapshotEdges(snapshot, edge, Material.Rail);
                    return snapshot;
                }
                catch { snapshot.Dispose(); throw; }
            }
        }

        private static void FillSnapshotEdges(Bitmap snapshot, int thickness, Color color)
        {
            // Do not spread captured DWM border or resize-margin pixels into the blurred image.
            using (var graphics = Graphics.FromImage(snapshot))
            using (var brush = new SolidBrush(color))
            {
                thickness = Math.Min(thickness + 1, Math.Min(snapshot.Width, snapshot.Height) / 2);
                graphics.FillRectangle(brush, 0, 0, snapshot.Width, thickness);
                graphics.FillRectangle(brush, 0, snapshot.Height - thickness, snapshot.Width, thickness);
                graphics.FillRectangle(brush, 0, 0, thickness, snapshot.Height);
                graphics.FillRectangle(brush, snapshot.Width - thickness, 0, thickness, snapshot.Height);
            }
        }

        private static Bitmap Blur(Bitmap source)
        {
            int width = Math.Max(1, Math.Min(480, source.Width / 4));
            int height = Math.Max(1, (int)Math.Round((double)source.Height * width / source.Width));
            var small = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(small))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, new Rectangle(Point.Empty, small.Size));
                }
                var data = small.LockBits(new Rectangle(Point.Empty, small.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] pixels = new byte[data.Stride * data.Height];
                    byte[] buffer = new byte[pixels.Length];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        BoxBlur(pixels, buffer, data.Width, data.Height, data.Stride, true);
                        BoxBlur(buffer, pixels, data.Width, data.Height, data.Stride, false);
                    }
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                finally { small.UnlockBits(data); }
                // Resample once when preparing the transition, never during an animation frame.
                var full = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                try
                {
                    using (var graphics = Graphics.FromImage(full))
                    using (var edges = new ImageAttributes())
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        edges.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(small, new Rectangle(Point.Empty, full.Size),
                            0, 0, small.Width, small.Height, GraphicsUnit.Pixel, edges);
                    }
                    return full;
                }
                catch { full.Dispose(); throw; }
            }
            finally { small.Dispose(); }
        }

        private static void BoxBlur(byte[] source, byte[] target, int width, int height, int stride, bool horizontal)
        {
            const int radius = 5;
            int lines = horizontal ? height : width;
            int length = horizontal ? width : height;
            int step = horizontal ? 4 : stride;
            for (int line = 0; line < lines; line++)
            {
                int start = horizontal ? line * stride : line * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    int sum = 0;
                    for (int k = -radius; k <= radius; k++)
                        sum += source[start + Math.Max(0, Math.Min(length - 1, k)) * step + channel];
                    for (int pixel = 0; pixel < length; pixel++)
                    {
                        target[start + pixel * step + channel] = (byte)(sum / (radius * 2 + 1));
                        sum -= source[start + Math.Max(0, pixel - radius) * step + channel];
                        sum += source[start + Math.Min(length - 1, pixel + radius + 1) * step + channel];
                    }
                }
            }
        }

        private void Tick(object sender, EventArgs e)
        {
            if (!finalFrame && elapsed.Elapsed.TotalMilliseconds < Duration)
            {
                Invalidate();
                return;
            }

            if (!finalFrame)
            {
                finalFrame = true;
                Invalidate();
                Update();
                Present();
                handoff.Start();
            }

            if (hostCovered && handoff.Elapsed.TotalMilliseconds < HandoffDuration)
            {
                double progress = handoff.Elapsed.TotalMilliseconds / HandoffDuration;
                double opacity = progress * progress * (3 - 2 * progress);
                ((FirstPaintWindow)host).SetTransitionOpacity((byte)Math.Round(opacity * 255));
                return;
            }
            Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (frames == null) e.Graphics.DrawImageUnscaled(before, 0, 0);
            else frames.Draw(e.Graphics, origin, maximumRadius,
                finalFrame ? 1 : (float)Math.Min(1, elapsed.Elapsed.TotalMilliseconds / Duration));
            base.OnPaint(e);
        }

        private void Interrupted(object sender, EventArgs e) { Dispose(); }
        private void Moved(object sender, EventArgs e)
        {
            if (!IsDisposed) Location = FrameBounds(host).Location;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                if (active == this) active = null;
                host.ClientSizeChanged -= Interrupted;
                host.VisibleChanged -= Interrupted;
                host.LocationChanged -= Moved;
                clock.Dispose();
                if (frames != null) frames.Dispose();
                // Finish painting the actual theme before taking away the covering window.
                if (!host.IsDisposed && host.IsHandleCreated)
                {
                    try
                    {
                        Material.UpdateChrome(host);
                        PagePainting.Repaint(host);
                    }
                    finally
                    {
                        if (hostCovered) ((FirstPaintWindow)host).SetTransitionOpacity(255);
                        Present();
                    }
                }
                before.Dispose();
                if (after != null) after.Dispose();
                if (blurredAfter != null) blurredAfter.Dispose();
            }
            base.Dispose(disposing);
            if (disposing && outline != null) outline.Dispose();
        }
    }
}
