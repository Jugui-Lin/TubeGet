using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed class TrayMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Material.Card);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Material.Line))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Material.Fill(e.Graphics, new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2),
                MainForm.Ui(8), Material.Accent);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Material.Line))
                e.Graphics.DrawLine(pen, MainForm.Ui(9), y, e.Item.Width - MainForm.Ui(9), y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            string glyph = e.Item.Tag as string;
            bool summary = glyph == "active" || glyph == "idle";
            Color color = summary || !e.Item.Enabled ? Material.Muted :
                e.Item.Selected ? Material.OnAccent : Material.Ink;
            int iconSize = MainForm.Ui(16);
            var icon = new Rectangle(MainForm.Ui(11), (e.Item.Height - iconSize) / 2, iconSize, iconSize);
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
            int textLeft = MainForm.Ui(37);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
                new Rectangle(textLeft, 0, e.Item.Width - textLeft - MainForm.Ui(8), e.Item.Height),
                color, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        }
    }
}
