using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed class MaterialPopupRenderer : ToolStripRenderer
    {
        private readonly bool rounded;

        internal MaterialPopupRenderer(bool rounded = false) { this.rounded = rounded; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Material.Card);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Material.Line))
            {
                var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                if (!rounded) e.Graphics.DrawRectangle(pen, bounds);
                else
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var outline = Material.Shape(bounds, MainForm.Ui(12))) e.Graphics.DrawPath(pen, outline);
                }
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = rounded ? InsetRow(e.Item) : new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2);
            if (bounds.Width > 0 && bounds.Height > 0)
                Material.Fill(e.Graphics, bounds, MainForm.Ui(8), Material.Accent);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            var bounds = rounded ? InsetRow(e.Item) : Rectangle.Inflate(new Rectangle(Point.Empty, e.Item.Size), -MainForm.Ui(9), 0);
            using (var pen = new Pen(Material.Line))
                e.Graphics.DrawLine(pen, bounds.Left, y, bounds.Right - (rounded ? 1 : 0), y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            string glyph = e.Item.Tag as string;
            bool summary = glyph == "active" || glyph == "idle";
            Color color = summary || !e.Item.Enabled ? Material.Muted :
                e.Item.Selected ? Material.OnAccent : Material.Ink;
            int iconSize = MainForm.Ui(16);
            var row = rounded ? InsetRow(e.Item) : new Rectangle(Point.Empty, e.Item.Size);
            var icon = new Rectangle(row.Left + MainForm.Ui(rounded ? 9 : 11),
                row.Top + (row.Height - iconSize) / 2, iconSize, iconSize);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (summary)
            {
                int dot = MainForm.Ui(8);
                using (var brush = new SolidBrush(glyph == "active" ? Material.Primary : Material.Muted))
                    e.Graphics.FillEllipse(brush, icon.X + (iconSize - dot) / 2, icon.Y + (iconSize - dot) / 2, dot, dot);
            }
            else if (glyph == "window" || glyph == "exit")
            {
                Rectangle mark = Rectangle.Inflate(icon, -MainForm.Ui(2), -MainForm.Ui(2));
                using (var pen = new Pen(color, MainForm.Ui(1)))
                {
                    if (glyph == "window") e.Graphics.DrawRectangle(pen, mark);
                    else
                    {
                        e.Graphics.DrawLine(pen, mark.Left, mark.Top, mark.Right, mark.Bottom);
                        e.Graphics.DrawLine(pen, mark.Right, mark.Top, mark.Left, mark.Bottom);
                    }
                }
            }
            else Material.Icon(e.Graphics, glyph, icon, color);
            int textLeft = row.Left + MainForm.Ui(rounded ? 35 : 37);
            var menuItem = e.Item as ToolStripMenuItem;
            bool isChecked = menuItem != null && menuItem.Checked;
            if (isChecked)
                Material.Icon(e.Graphics, "done", new Rectangle(row.Right - MainForm.Ui(27), icon.Y, iconSize, iconSize), color);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
                new Rectangle(textLeft, row.Top, row.Right - textLeft - MainForm.Ui(isChecked ? 34 : 8), row.Height),
                color, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        }

        // WinForms can leave extra width after the fixed-size items. Measure from the popup edges.
        private static Rectangle InsetRow(ToolStripItem item)
        {
            var bounds = item.Bounds;
            var owner = item.Owner;
            int side = System.Math.Max(MainForm.Ui(12), System.Math.Max(bounds.Left, owner.ClientSize.Width - bounds.Right));
            int top = System.Math.Max(MainForm.Ui(2), MainForm.Ui(8) - bounds.Top);
            int bottom = System.Math.Min(bounds.Height - MainForm.Ui(2), owner.ClientSize.Height - MainForm.Ui(8) - bounds.Top);
            return Rectangle.FromLTRB(side - bounds.Left, top, owner.ClientSize.Width - side - bounds.Left, bottom);
        }
    }
}
