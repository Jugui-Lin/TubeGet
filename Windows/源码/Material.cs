using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TubeGet
{
    internal static class Material
    {
        internal static bool Dark { get; private set; }
        internal static Color Surface { get { return Tone(0xF6F6FC, 0x11151A); } }
        internal static Color Rail { get { return Tone(0xECEEFA, 0x161B22); } }
        internal static Color LinkCard { get { return Tone(0xE3E4FA, 0x202B38); } }
        internal static Color Card { get { return Tone(0xFDFDFF, 0x1A2028); } }
        internal static Color Container { get { return Tone(0xEFF0FA, 0x222B36); } }
        internal static Color Primary { get { return Tone(0x444D9B, 0x78D1E4); } }
        internal static Color OnPrimary { get { return Tone(0xFFFFFF, 0x0B2631); } }
        internal static Color Accent { get { return Tone(0xDBDEF9, 0x293D49); } }
        internal static Color OnAccent { get { return Tone(0x283371, 0xE4F5FA); } }
        internal static Color Ink { get { return Tone(0x1C223E, 0xE9EEF5); } }
        internal static Color Muted { get { return Tone(0x4A536B, 0xB8C4D3); } }
        internal static Color Line { get { return Tone(0xDADDF0, 0x36424F); } }
        internal static Color Error { get { return Tone(0x913B40, 0xFFB8B4); } }
        internal static Color ErrorContainer { get { return Tone(0xFBE8E6, 0x452A2B); } }
        internal static Color Success { get { return Tone(0x245C4F, 0x9ADCC8); } }
        internal static Color Positive { get { return Tone(0x247448, 0x83DBA4); } }
        internal static Color OnPositive { get { return Tone(0xFFFFFE, 0x103521); } }
        internal static Color SuccessContainer { get { return Tone(0xE1F3E8, 0x213B30); } }

        internal static bool ResolveDark(string mode)
        {
            return mode == "dark" || (mode != "light" && ReadDark());
        }

        internal static void SetTheme(bool dark) { Dark = dark; }

        internal static Color[] Palette()
        {
            return new[] { Surface, Rail, LinkCard, Card, Container, Primary, OnPrimary, Accent, OnAccent, Ink, Muted, Line,
                Error, ErrorContainer, Success, SuccessContainer, Positive, OnPositive };
        }

        private static bool ReadDark()
        {
            try
            {
                object setting = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return setting is int && (int)setting == 0;
            }
            catch (System.Security.SecurityException) { return false; }
        }

        private static Color Tone(int light, int dark)
        {
            int rgb = Dark ? dark : light;
            return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal static void ThemeWindow(Form window, bool disableTransitions = false)
        {
            window.HandleCreated += (s, e) =>
            {
                if (disableTransitions)
                {
                    const int TransitionsForcedDisabled = 3;
                    int enabled = 1;
                    DwmSetWindowAttribute(window.Handle, TransitionsForcedDisabled, ref enabled, sizeof(int));
                }
                UpdateChrome(window);
            };
        }

        internal static void UpdateChrome(Form window)
        {
            if (!window.IsHandleCreated) return;
            int enabled = Dark ? 1 : 0;
            DwmSetWindowAttribute(window.Handle, 20, ref enabled, sizeof(int));
            int caption = ColorTranslator.ToWin32(Rail);
            int text = ColorTranslator.ToWin32(Ink);
            DwmSetWindowAttribute(window.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(window.Handle, 36, ref text, sizeof(int));
        }

        internal static void HideWindowBorder(Form window)
        {
            const int BorderColor = 34;
            int value = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE retains native corner rounding.
            DwmSetWindowAttribute(window.Handle, BorderColor, ref value, sizeof(int));
        }

        internal static void DisableWindowDecorations(Form window)
        {
            const int NonClientRenderingPolicy = 2;
            int disabled = 1;
            DwmSetWindowAttribute(window.Handle, NonClientRenderingPolicy, ref disabled, sizeof(int));
        }

        internal static GraphicsPath Shape(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void Fill(Graphics g, RectangleF bounds, float radius, Color color, Color? border = null)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = Shape(bounds, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
                if (border.HasValue) using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
            }
        }

        internal static void Icon(Graphics g, string name, Rectangle bounds, Color color)
        {
            var old = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            using (var p = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (name)
                {
                    case "download":
                        g.DrawLine(p, 12, 3, 12, 15);
                        g.DrawLines(p, new[] { new Point(7, 10), new Point(12, 15), new Point(17, 10) });
                        g.DrawLines(p, new[] { new Point(4, 17), new Point(4, 20), new Point(20, 20), new Point(20, 17) });
                        break;
                    case "done":
                        g.DrawEllipse(p, 3, 3, 18, 18);
                        g.DrawLines(p, new[] { new Point(7, 12), new Point(10, 15), new Point(17, 8) });
                        break;
                    case "convert":
                        g.DrawLines(p, new[] { new Point(3, 8), new Point(20, 8), new Point(16, 4) });
                        g.DrawLines(p, new[] { new Point(21, 16), new Point(4, 16), new Point(8, 20) });
                        break;
                    case "folder":
                        g.DrawLines(p, new[] { new Point(3, 6), new Point(9, 6), new Point(11, 8), new Point(21, 8),
                            new Point(21, 19), new Point(3, 19), new Point(3, 6) });
                        break;
                    case "queue":
                        g.DrawLine(p, 7, 6, 21, 6); g.DrawLine(p, 7, 12, 21, 12); g.DrawLine(p, 7, 18, 17, 18);
                        for (int y = 5; y < 19; y += 6) g.FillEllipse(brush, 3, y, 2, 2);
                        break;
                    case "back":
                        g.DrawLines(p, new[] { new Point(11, 5), new Point(4, 12), new Point(11, 19) });
                        g.DrawLine(p, 4, 12, 20, 12);
                        break;
                    case "grid":
                        g.DrawRectangle(p, 3, 3, 7, 7);
                        g.DrawRectangle(p, 14, 3, 7, 7);
                        g.DrawRectangle(p, 3, 14, 7, 7);
                        g.DrawRectangle(p, 14, 14, 7, 7);
                        break;
                    case "speed":
                        g.DrawArc(p, 3, 4, 18, 18, 180, 180);
                        g.DrawLine(p, 12, 13, 16, 9);
                        g.FillEllipse(brush, 10.5f, 11.5f, 3, 3);
                        g.DrawLine(p, 6, 18, 18, 18);
                        break;
                    case "play":
                        g.FillPolygon(brush, new[] { new Point(8, 5), new Point(19, 12), new Point(8, 19) });
                        break;
                    case "pause":
                        g.DrawLine(p, 9, 5, 9, 19); g.DrawLine(p, 15, 5, 15, 19);
                        break;
                    case "clipboard":
                        g.DrawLines(p, new[] { new Point(8, 6), new Point(5, 6), new Point(5, 21),
                            new Point(19, 21), new Point(19, 6), new Point(16, 6) });
                        g.DrawRectangle(p, 8, 3, 8, 5);
                        break;
                    case "plus":
                        g.DrawLine(p, 5, 12, 19, 12); g.DrawLine(p, 12, 5, 12, 19);
                        break;
                    case "chevron":
                        g.DrawLines(p, new[] { new Point(7, 10), new Point(12, 15), new Point(17, 10) });
                        break;
                    case "more":
                        for (int y = 5; y <= 17; y += 6) g.FillEllipse(brush, 10.5f, y, 3, 3);
                        break;
                    case "delete":
                        g.DrawLine(p, 5, 6, 19, 6); g.DrawLine(p, 9, 3, 15, 3);
                        g.DrawLines(p, new[] { new Point(7, 7), new Point(8, 20), new Point(16, 20), new Point(17, 7) });
                        break;
                    case "settings":
                        g.DrawEllipse(p, 5, 5, 14, 14); g.DrawEllipse(p, 9, 9, 6, 6);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(p, 12 + 7 * (float)Math.Cos(a), 12 + 7 * (float)Math.Sin(a),
                                12 + 10 * (float)Math.Cos(a), 12 + 10 * (float)Math.Sin(a));
                        }
                        break;
                    case "theme":
                        if (Dark)
                        {
                            g.DrawEllipse(p, 7, 7, 10, 10);
                            for (int i = 0; i < 8; i++)
                            {
                                double angle = i * Math.PI / 4;
                                float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                                g.DrawLine(p, 12 + 8 * x, 12 + 8 * y, 12 + 10 * x, 12 + 10 * y);
                            }
                        }
                        else
                        {
                            using (var moon = new GraphicsPath())
                            {
                                moon.AddBezier(12, 3, 7, 14, 16, 19, 21, 14);
                                moon.AddBezier(21, 14, 18, 25, 3, 23, 3, 13);
                                moon.AddBezier(3, 13, 3, 8, 7, 4, 12, 3);
                                moon.CloseFigure();
                                g.FillPath(brush, moon);
                            }
                        }
                        break;
                    case "search":
                        g.DrawEllipse(p, 4, 4, 11, 11); g.DrawLine(p, 14, 14, 20, 20);
                        break;
                }
            }
            g.Restore(old);
        }

        internal static Rectangle ComposerCheckBounds(Rectangle row)
        {
            int size = MainForm.Ui(18);
            return new Rectangle(row.Left + MainForm.Ui(4), row.Top + (row.Height - size) / 2, size, size);
        }

        internal static void DrawComposerCheck(Graphics graphics, Rectangle box, CheckState state, bool enabled)
        {
            bool marked = state != CheckState.Unchecked;
            Color border = enabled ? Tone(0x8293AC, 0x8597AE) : Line;
            Color background = marked ? (enabled ? Tone(0x145FA9, 0x206CB4) : Muted) : Card;
            var saved = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Fill(graphics, box, MainForm.Ui(4), background, border);
            if (marked)
            {
                using (var pen = new Pen(Color.White, MainForm.Ui(2)) {
                    StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
                })
                {
                    if (state == CheckState.Indeterminate)
                        graphics.DrawLine(pen, box.Left + box.Width * .27f, box.Top + box.Height * .5f,
                            box.Left + box.Width * .73f, box.Top + box.Height * .5f);
                    else graphics.DrawLines(pen, new[] {
                        new PointF(box.Left + box.Width * .23f, box.Top + box.Height * .51f),
                        new PointF(box.Left + box.Width * .42f, box.Top + box.Height * .70f),
                        new PointF(box.Left + box.Width * .77f, box.Top + box.Height * .31f)
                    });
                }
            }
            graphics.Restore(saved);
        }

        internal static void Field(Control control)
        {
            control.BackColor = Container;
            control.ForeColor = Ink;
            var text = control as TextBox;
            if (text != null) text.BorderStyle = BorderStyle.FixedSingle;
            var combo = control as ComboBox;
            if (combo != null) combo.FlatStyle = FlatStyle.Flat;
        }
    }

    internal enum ButtonStyle { Tonal, Filled, Quiet, Navigation, Outlined }

    internal sealed class MaterialButton : Button
    {
        private bool hover, pressed;
        internal string Glyph { get; set; }
        internal ButtonStyle Appearance { get; set; }
        internal bool Current { get; set; }

        internal MaterialButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Material.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            bool navigation = Appearance == ButtonStyle.Navigation;
            bool filled = Appearance == ButtonStyle.Filled;
            // Keep the theme trigger filled through snapshot capture and overlay removal,
            // including when the pointer moves away while the circular reveal is running.
            bool themeTrigger = Glyph == "theme";
            bool highlighted = !themeTrigger && hover;
            Color ink = !Enabled ? Material.Muted : filled ? Material.OnPrimary :
                navigation && Current ? Material.OnAccent : Material.Ink;
            Color fill = filled ? Material.Primary : Appearance == ButtonStyle.Tonal ? Material.Container : Color.Transparent;
            if (themeTrigger) fill = Material.Accent;
            if (!Enabled) fill = Material.Container;
            if (highlighted && Enabled && !filled && !navigation) fill = Material.Accent;
            if (navigation)
            {
                if (Current || highlighted) Material.Fill(g, bounds, 20, Current ? Material.Accent : Material.Container);
                int iconSize = MainForm.Ui(21);
                int textStart = MainForm.Ui(44);
                Material.Icon(g, Glyph, new Rectangle(MainForm.Ui(14), (Height - iconSize) / 2, iconSize, iconSize), ink);
                DrawText(g, new Rectangle(textStart, 0, Math.Max(1, Width - textStart - MainForm.Ui(6)), Height), ink, false);
            }
            else
            {
                Material.Fill(g, bounds, 20, fill, Appearance == ButtonStyle.Outlined ? Material.Line : (Color?)null);
                if (filled && highlighted) Material.Fill(g, bounds, 17, Color.FromArgb(20, ink));
                if (pressed && !themeTrigger) Material.Fill(g, bounds, 17, Color.FromArgb(25, ink));
                int textWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                if (!String.IsNullOrEmpty(Glyph))
                {
                    int iconSize = MainForm.Ui(18);
                    int gap = MainForm.Ui(5);
                    int x = String.IsNullOrEmpty(Text) ? (Width - iconSize) / 2 :
                        Math.Max(MainForm.Ui(5), (Width - textWidth - iconSize - gap) / 2);
                    Material.Icon(g, Glyph, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize), ink);
                    if (Text.Length > 0)
                    {
                        int start = x + iconSize + gap;
                        DrawText(g, new Rectangle(start, 0, Math.Max(1, Width - start - MainForm.Ui(4)), Height), ink, false);
                    }
                }
                else DrawText(g, ClientRectangle, ink);
            }
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Material.Primary, 1.5f))
                using (var shape = Material.Shape(Rectangle.Inflate(bounds, -2, -2), 14))
                    g.DrawPath(pen, shape);
        }

        private void DrawText(Graphics g, Rectangle bounds, Color ink, bool centered = true)
        {
            TextRenderer.DrawText(g, Text, Font, bounds, ink,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                (centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        }
    }

    internal sealed class MaterialCard : Panel
    {
        internal Color FillColor { get; set; }
        internal bool Border { get; set; }
        internal int Radius { get; set; }

        internal MaterialCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Material.Surface;
            FillColor = Material.Card;
            Border = true;
            Radius = 23;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Material.Fill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Radius, FillColor, Border ? Material.Line : (Color?)null);
        }
    }

    internal sealed class EmptyView : Control
    {
        private string glyph, title, hint;
        internal EmptyView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ResizeRedraw = true;
            BackColor = Material.Card;
            AccessibleRole = AccessibleRole.StaticText;
        }

        internal void ShowMessage(string icon, string heading, string description)
        {
            if (icon == glyph && heading == title && description == hint) return;
            glyph = icon; title = heading; hint = description;
            Text = heading;
            AccessibleName = heading + " " + description;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = Width / 2, y = Math.Max(MainForm.Ui(7), (Height - MainForm.Ui(136)) / 2);
            Material.Fill(g, new Rectangle(x - MainForm.Ui(32), y, MainForm.Ui(64), MainForm.Ui(64)), MainForm.Ui(22), Material.Accent);
            Material.Icon(g, glyph, new Rectangle(x - MainForm.Ui(15), y + MainForm.Ui(17), MainForm.Ui(30), MainForm.Ui(30)), Material.OnAccent);
            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            using (var titleFont = MainForm.UiFont(15, FontStyle.Bold))
                TextRenderer.DrawText(g, title, titleFont, new Rectangle(MainForm.Ui(10), y + MainForm.Ui(77), Width - MainForm.Ui(20), MainForm.Ui(30)), Material.Ink, flags);
            TextRenderer.DrawText(g, hint, Font, new Rectangle(MainForm.Ui(10), y + MainForm.Ui(109), Width - MainForm.Ui(20), MainForm.Ui(27)), Material.Muted, flags);
        }
    }

    internal sealed class MaterialMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Material.Card; } }
        public override Color ImageMarginGradientBegin { get { return Material.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Material.Card; } }
        public override Color ImageMarginGradientEnd { get { return Material.Card; } }
        public override Color MenuItemSelected { get { return Material.Accent; } }
        public override Color MenuItemBorder { get { return Material.Accent; } }
        public override Color MenuBorder { get { return Material.Line; } }
        public override Color SeparatorDark { get { return Material.Line; } }
        public override Color SeparatorLight { get { return Material.Line; } }
        public override Color CheckBackground { get { return Material.Accent; } }
        public override Color CheckSelectedBackground { get { return Material.Accent; } }
    }
}
