using System;
using System.Drawing;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private sealed class PreferenceCard
        {
            private readonly TableLayoutPanel fields;
            internal readonly MaterialCard View;

            internal PreferenceCard(string title)
            {
                View = new MaterialCard { Dock = DockStyle.Fill, Padding = new Padding(15, 10, 15, 8), Margin = new Padding(0, 0, 0, 0) };
                var heading = new Label {
                    Text = title, Dock = DockStyle.Top, Height = 31, TextAlign = ContentAlignment.MiddleLeft,
                    Font = UiFont(12, FontStyle.Bold),
                    BackColor = Material.Card, ForeColor = Material.Ink
                };
                fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, BackColor = Material.Card };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                View.Controls.Add(fields);
                View.Controls.Add(heading);
            }

            internal void Row(Control item, int height)
            {
                int row = fields.RowCount - 1;
                fields.RowCount++;
                fields.RowStyles.Insert(row, new RowStyle(SizeType.Absolute, height));
                item.Dock = DockStyle.Fill;
                item.Margin = Padding.Empty;
                fields.Controls.Add(item, 0, row);
            }

            internal Label Add(string label, Control item, Button browse = null)
            {
                var line = new Panel { BackColor = Material.Card, Padding = new Padding(0, 0, 0, 5) };
                var caption = new Label {
                    Text = label, Dock = DockStyle.Top, Height = 20,
                    ForeColor = Material.Muted, TextAlign = ContentAlignment.MiddleLeft,
                    Font = UiFont(9)
                };
                item.AccessibleName = label;
                var entry = item is TextBox ? new SettingField((TextBox)item) : item;
                entry.Dock = DockStyle.Fill;
                var inputs = new Panel { Dock = DockStyle.Fill, BackColor = Material.Card };
                inputs.Controls.Add(entry);
                if (browse != null)
                {
                    var end = new Panel { Dock = DockStyle.Right, Width = 72, Padding = new Padding(5, 0, 0, 0) };
                    browse.Dock = DockStyle.Fill;
                    end.Controls.Add(browse);
                    inputs.Controls.Add(end);
                }
                line.Controls.Add(inputs);
                line.Controls.Add(caption);
                Row(line, 53);
                return caption;
            }

            internal TextBox Text(string label, string value)
            {
                var input = new TextBox { Text = value ?? "" };
                Add(label, input);
                return input;
            }

            internal SettingPicker Select(string label, string[] items, string value)
            {
                var input = new SettingPicker(items, value);
                Add(label, input);
                return input;
            }

            internal CheckBox Check(string label, bool value)
            {
                var input = new SettingToggle { Text = label, Checked = value };
                Row(input, 33);
                return input;
            }
        }

        private static Control PreferencePage(params PreferenceCard[] cards)
        {
            var columns = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = cards.Length, RowCount = 1, Padding = new Padding(12, 12, 15, 12)
            };
            for (int i = 0; i < cards.Length; i++)
            {
                columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cards.Length));
                if (i < cards.Length - 1) cards[i].View.Margin = new Padding(0, 0, 11, 0);
                columns.Controls.Add(cards[i].View, i, 0);
            }
            return columns;
        }

        private Control PreferencesNavigation(int initial, params Control[] pages)
        {
            var frame = new Panel { Dock = DockStyle.Fill, BackColor = Material.Surface };
            var rail = new FlowLayoutPanel {
                Dock = DockStyle.Left, Width = 126, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(9, 12, 8, 0), BackColor = Material.Rail, TabIndex = 0
            };
            var content = new Panel { Dock = DockStyle.Fill, TabIndex = 1 };
            var labels = new[] { "下载", "转换", "网络", "常规", "组件", "AI 修复" };
            var icons = new[] { "download", "convert", "speed", "settings", "download", "search" };
            var buttons = new MaterialButton[pages.Length];
            int selected = -1;
            Action<int> select = index =>
            {
                if (index == selected) return;
                PagePainting.Update(content, () =>
                {
                    pages[index].Visible = true;
                    pages[index].BringToFront();
                    for (int i = 0; i < pages.Length; i++)
                        if (i != index) pages[i].Visible = false;
                });
                selected = index;
                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].Current = i == selected;
                    buttons[i].Invalidate();
                }
            };
            for (int i = 0; i < pages.Length; i++)
            {
                int index = i;
                buttons[i] = (MaterialButton)ButtonOf(labels[i], 108, () => select(index), null, icons[i], ButtonStyle.Navigation);
                buttons[i].Margin = new Padding(0, 0, 0, 6);
                rail.Controls.Add(buttons[i]);
                content.Controls.Add(pages[i]);
            }
            frame.Controls.Add(content);
            frame.Controls.Add(rail);
            select(initial);
            return frame;
        }
    }
}
