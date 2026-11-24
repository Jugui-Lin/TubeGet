using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed class SettingPicker : Button
    {
        private string[] choices;
        private int selected = -1;
        internal event EventHandler SelectionChanged;

        internal SettingPicker(string[] items, string initial)
        {
            choices = items;
            SetIndex(Math.Max(0, Array.IndexOf(items, initial)));
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ComboBox;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        internal int SelectedIndex { get { return selected; } }

        internal void SetChoices(string[] items, string initial)
        {
            choices = items;
            selected = -1;
            SetIndex(Math.Max(0, Array.IndexOf(items, initial)));
        }

        internal void Choose(int index) { SetIndex(index); }

        private void SetIndex(int index)
        {
            if (selected == index) return;
            selected = index;
            Text = choices[index];
            AccessibleDescription = "当前值：" + Text;
            Invalidate();
            var changed = SelectionChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var menu = new ContextMenuStrip {
                Font = Font, BackColor = Material.Card, ForeColor = Material.Ink,
                Renderer = new ToolStripProfessionalRenderer(new MaterialMenuColors())
            };
            for (int i = 0; i < choices.Length; i++)
            {
                int choice = i;
                var entry = new ToolStripMenuItem(choices[i]) { Checked = selected == i };
                entry.Click += (s, a) => SetIndex(choice);
                menu.Items.Add(entry);
            }
            menu.Closed += (s, a) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, new Point(0, Height));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F4 || keyData == (Keys.Alt | Keys.Down))
            { OnClick(EventArgs.Empty); return true; }
            if (keyData == Keys.Up || keyData == Keys.Down)
            {
                SetIndex(Math.Max(0, Math.Min(choices.Length - 1, selected + (keyData == Keys.Up ? -1 : 1))));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Material.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), 9, Material.Container, Focused ? Material.Primary : Material.Line);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(10, 0, Width - 36, Height), Material.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            Material.Icon(g, "chevron", new Rectangle(Width - 25, (Height - 18) / 2, 18, 18), Material.Muted);
        }
    }

    internal sealed class SettingToggle : CheckBox
    {
        internal SettingToggle()
        {
            AutoSize = false;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = (Height - 19) / 2;
            int x = Width - 35;
            Material.Fill(g, new Rectangle(x, y, 34, 19), 9, Checked ? Material.Primary : Material.Container,
                Focused ? Material.Primary : Material.Line);
            using (var thumb = new SolidBrush(Checked ? Material.OnPrimary : Material.Muted))
                g.FillEllipse(thumb, x + (Checked ? 18 : 4), y + 3, 13, 13);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width - 45, Height), Material.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class ComposerSelectionBox : CheckBox
    {
        internal ComposerSelectionBox()
        {
            AccessibleName = "全选／取消全选";
            AutoCheck = false;
            AutoSize = false;
            ThreeState = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnCheckStateChanged(EventArgs e) { base.OnCheckStateChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent.BackColor);
            var box = Material.ComposerCheckBounds(ClientRectangle);
            Material.DrawComposerCheck(g, box, CheckState, Enabled);
            if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(box, 2, 2), Material.Ink, Parent.BackColor);
        }
    }

    internal sealed class SettingField : Panel
    {
        private readonly TextBox input;
        internal SettingField(TextBox text)
        {
            input = text;
            BackColor = Material.Container;
            Padding = new Padding(10, 6, 9, 0);
            input.BorderStyle = BorderStyle.None;
            input.Dock = DockStyle.Top;
            input.BackColor = Material.Container;
            input.ForeColor = Material.Ink;
            Controls.Add(input);
            input.GotFocus += (s, e) => Invalidate();
            input.LostFocus += (s, e) => Invalidate();
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Material.Card);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Material.Fill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 9,
                Material.Container, input.Focused ? Material.Primary : Material.Line);
        }
    }
}
