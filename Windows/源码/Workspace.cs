using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private static readonly string[] PageNames = { "下载工作台", "已完成", "媒体转换" };
        private static readonly string[] PageIcons = { "download", "done", "convert" };
        private readonly Label heading = new Label();
        private readonly Label count = new Label();
        private readonly Label activity = new Label();
        private readonly Panel heroActions = new Panel();
        private readonly Panel workspaceFooter = new Panel();
        private readonly Panel taskListHost = new Panel();
        private readonly Label downloadQueueCount = new Label();
        private readonly Label downloadStatus = new Label();
        private readonly MaterialCard downloadStatusBadge = new MaterialCard();
        private readonly MaterialCard taskCard = new MaterialCard();
        private ImageList rowHeight;

        private void BuildWorkspace()
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 211, BackColor = Material.Rail };
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(11, 11, 12, 12);
            BackColor = Material.Rail;
            Controls.Add(body);
            Controls.Add(sidebar);
            BuildRail(sidebar);

            taskCard.Dock = DockStyle.Fill;
            taskCard.BackColor = Material.Rail;
            taskCard.Padding = new Padding(21, 6, 21, 8);
            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Material.Card };
            var title = new FlowLayoutPanel {
                Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 14, 0, 0), BackColor = Material.Card
            };
            heading.AutoSize = true;
            heading.Font = UiFont(23, FontStyle.Bold);
            heading.Margin = new Padding(0, 0, 11, 0);
            heading.ForeColor = Material.Ink;
            count.AutoSize = true;
            count.Margin = new Padding(0, 12, 0, 0);
            count.ForeColor = Material.Muted;
            title.Controls.Add(heading);
            title.Controls.Add(count);
            heroActions.Dock = DockStyle.Right;
            heroActions.Width = 282;
            heroActions.BackColor = Material.Card;
            downloadStatusBadge.Radius = 18;
            downloadStatusBadge.Border = false;
            downloadStatusBadge.FillColor = Material.Accent;
            downloadStatus.Dock = DockStyle.Fill;
            downloadStatus.TextAlign = ContentAlignment.MiddleCenter;
            downloadStatus.ForeColor = Material.OnAccent;
            downloadStatus.BackColor = Color.Transparent;
            downloadStatusBadge.Controls.Add(downloadStatus);
            Disposed += (s, e) => downloadStatusBadge.Dispose();
            header.Controls.Add(title);
            header.Controls.Add(heroActions);

            actionBar.Dock = DockStyle.Top;
            actionBar.Height = 52;
            actionBar.BackColor = Material.Card;
            completedViewSlot.Dock = DockStyle.Right;
            completedViewSlot.Width = 40;
            completedViewSlot.Padding = new Padding(0, 1, 0, 0);
            completedViewSlot.Visible = false;
            bar.Dock = DockStyle.Fill;
            bar.Padding = new Padding(0, 1, 0, 9);
            bar.WrapContents = false;
            bar.BackColor = Material.Card;
            actionBar.Controls.Add(bar);
            actionBar.Controls.Add(completedViewSlot);

            var footer = workspaceFooter;
            footer.Dock = DockStyle.Bottom;
            footer.Height = 39;
            footer.BackColor = Material.Card;
            footer.Paint += (s, e) =>
            {
                using (var pen = new Pen(Material.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Material.Muted;
            status.AutoEllipsis = true;
            status.Font = UiFont(9);
            activity.Dock = DockStyle.Right;
            activity.Width = 262;
            activity.TextAlign = ContentAlignment.MiddleRight;
            activity.ForeColor = Material.Primary;
            activity.Font = UiFont(9);
            footer.Controls.Add(status);
            footer.Controls.Add(activity);

            ConfigureTasks();
            BuildComposer();
            taskCard.Controls.Add(composer);
            taskListHost.Dock = DockStyle.Fill;
            taskCard.Controls.Add(taskListHost);
            taskCard.Controls.Add(footer);
            taskCard.Controls.Add(actionBar);
            taskCard.Controls.Add(header);
            body.Controls.Add(taskCard);
            foreach (Control c in new Control[] { body, taskCard, list, empty, downloadQueueHost, composer }) SetupDrop(c);
        }

        private void BuildRail(Panel sidebar)
        {
            var brand = new Panel { Bounds = new Rectangle(14, 14, 184, 65), AccessibleName = "TubeGet" };
            brand.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawImageUnscaled(brandIcon, Ui(8), Ui(9));
                using (var type = UiFont(15, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, "TubeGet", type, new Rectangle(Ui(62), Ui(11), Ui(114), Ui(42)),
                        Material.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            sidebar.Controls.Add(brand);
            string[] labels = { "下载中", "已完成", "媒体转换" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = RailButton(labels[i], PageIcons[i], () => SetPage(index));
                button.Location = new Point(13, 94 + i * 52);
                sidebar.Controls.Add(button);
                nav.Add(button);
            }
            var folder = RailButton("输出目录", "folder", () => OpenFolder(page == 2 ? options.Converted : options.Downloads));
            var settings = RailButton("设置", "settings", () => Settings(page == 2 ? 1 : 0));
            sidebar.Controls.Add(folder);
            sidebar.Controls.Add(settings);
            folder.Location = new Point(13, sidebar.ClientSize.Height - 111);
            settings.Location = new Point(13, sidebar.ClientSize.Height - 59);
            folder.Anchor = settings.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        }

        private MaterialButton RailButton(string label, string icon, Action action)
        {
            var b = (MaterialButton)ButtonOf(label, 181, action, null, icon, ButtonStyle.Navigation);
            b.Height = 46;
            return b;
        }

        private void AddHeaderAction(string label, string icon, Action click, Func<ContextMenuStrip> variants = null)
        {
            var more = ButtonOf("", 38, null, "更多选项", "more", ButtonStyle.Quiet);
            int logicalWidth = layoutScaled ? (int)Math.Round(heroActions.Width / UiScale) : heroActions.Width;
            more.Location = new Point(logicalWidth - 38, 19);
            Attach(more, MainMenu, alignRight: true);
            heroActions.Controls.Add(more);
            var theme = ButtonOf("", 39, ToggleTheme, "切换浅色／深色", "theme", ButtonStyle.Tonal);
            theme.Location = new Point(more.Left - 44, 19);
            heroActions.Controls.Add(theme);
            int right = theme.Left - 9;
            if (variants != null)
            {
                var other = ButtonOf("", 38, null, "更多下载方式", "chevron", ButtonStyle.Outlined);
                other.Location = new Point(right - other.Width, 19);
                Attach(other, variants);
                heroActions.Controls.Add(other);
                right = other.Left - 6;
            }
            if (label == null)
            {
                downloadStatusBadge.Bounds = layoutScaled ? new Rectangle(Ui(right - 118), Ui(19), Ui(112), Ui(36)) :
                    new Rectangle(right - 118, 19, 112, 36);
                downloadStatusBadge.BackColor = heroActions.BackColor;
                downloadStatusBadge.FillColor = Material.Accent;
                downloadStatus.ForeColor = Material.OnAccent;
                heroActions.Controls.Add(downloadStatusBadge);
                return;
            }
            var primary = ButtonOf(label, label.Length > 5 ? 153 : 123, click, null, icon, ButtonStyle.Filled);
            primary.Location = new Point(right - primary.Width, 19);
            heroActions.Controls.Add(primary);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string text);

        private void ConfigureActions()
        {
            heroActions.SuspendLayout();
            actionBar.SuspendLayout();
            bar.SuspendLayout();
            heroActions.Controls.Remove(downloadStatusBadge);
            while (heroActions.Controls.Count > 0) heroActions.Controls[0].Dispose();
            while (bar.Controls.Count > 0) bar.Controls[0].Dispose();
            if (completedViewButton != null) completedViewButton.Dispose();
            completedViewButton = null;
            bool downloading = page == DownloadPage;
            actionBar.Visible = workspaceFooter.Visible = !downloading;
            completedViewSlot.Visible = page == 1;
            if (downloading) AddHeaderAction(null, null, null, DownloadMenu);
            if (page == 1)
            {
                AddHeaderAction("打开下载目录", "folder", () => OpenFolder(completedFolder ?? options.Downloads));
                if (completedFolder != null)
                    bar.Controls.Add(ButtonOf("返回", 80, () => NavigateCompletedFolder(null), "返回已完成", "back", ButtonStyle.Outlined));
                var searchBox = new Panel { Width = 254, Height = 36, BackColor = Material.Container, Margin = new Padding(0, 0, 10, 0) };
                searchBox.Paint += (s, e) => Material.Icon(e.Graphics, "search", new Rectangle(10, 8, 18, 18), Material.Muted);
                var search = new TextBox {
                    Width = 210, Location = new Point(35, 8), BorderStyle = BorderStyle.None,
                    BackColor = Material.Container, ForeColor = Material.Ink, AccessibleName = "搜索已完成的任务"
                };
                search.Text = filter;
                search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, IntPtr.Zero, search.AccessibleName);
                search.TextChanged += (s, e) => { filter = search.Text.Trim(); RefreshTasks(); };
                searchBox.Controls.Add(search);
                bar.Controls.Add(searchBox);
                completedViewButton = (MaterialButton)ButtonOf("", 40, ToggleCompletedView, null, null, ButtonStyle.Outlined);
                if (layoutScaled) ScaleDesign(completedViewButton);
                completedViewButton.Dock = DockStyle.Top;
                UpdateCompletedViewButton();
                completedViewSlot.Controls.Add(completedViewButton);
                bar.Controls.Add(ButtonOf("", 40, () => DeleteJobs(Selected()), "删除选中任务", "delete"));
            }
            if (page == 2)
            {
                AddHeaderAction("导入媒体", "plus", ImportMedia);
                AddQueueButtons();
                bar.Controls.Add(new Label {
                    Text = "输出格式", AutoSize = true, ForeColor = Material.Muted, Margin = new Padding(11, 8, 5, 0)
                });
                var formats = new SettingPicker(ConversionFormats, conversionFormat);
                formats.Size = new Size(81, 34);
                formats.Margin = new Padding(0, 1, 3, 0);
                formats.AccessibleName = "输出格式";
                formats.SelectionChanged += (s, e) =>
                {
                    conversionFormat = options.ConvertFormat = formats.Text;
                    SaveOptions();
                    foreach (var j in EditableConversions()) j.Format = conversionFormat;
                    Changed();
                };
                bar.Controls.Add(formats);
                var copy = new SettingToggle {
                    Text = "无损转换", Width = 120, Height = 36, Checked = streamCopy,
                    ForeColor = Material.Muted, Margin = new Padding(11, 0, 4, 0)
                };
                copy.CheckedChanged += (s, e) =>
                {
                    streamCopy = options.StreamCopy = copy.Checked;
                    SaveOptions();
                    foreach (var j in EditableConversions()) j.CopyStreams = streamCopy;
                    Changed();
                };
                bar.Controls.Add(copy);
                bar.Controls.Add(ButtonOf("转码参数", 98, EditTranscode, null, "settings", ButtonStyle.Outlined));
            }
            if (layoutScaled)
            {
                foreach (Control control in bar.Controls) ScaleDesign(control);
                foreach (Control control in heroActions.Controls)
                    if (control != downloadStatusBadge) ScaleDesign(control);
            }
            bar.ResumeLayout();
            actionBar.ResumeLayout();
            heroActions.ResumeLayout();
        }

        private MaterialCard BuildDownloadQueue()
        {
            var card = new MaterialCard {
                Dock = DockStyle.Fill, Radius = 14, BackColor = Material.Card, Margin = new Padding(5, 0, 0, 0), Padding = new Padding(12, 8, 12, 10)
            };
            var header = new Panel { Dock = DockStyle.Top, Height = 41, BackColor = Material.Card };
            var title = ComposerSectionTitle("下载队列");
            title.Dock = DockStyle.Left;
            title.Width = 78;
            downloadQueueCount.Dock = DockStyle.Fill;
            downloadQueueCount.TextAlign = ContentAlignment.MiddleLeft;
            downloadQueueCount.ForeColor = Material.Muted;
            downloadQueueCount.AutoEllipsis = true;
            var actions = new Panel { Dock = DockStyle.Right, Width = 218, BackColor = Material.Card, Padding = new Padding(0, 0, 0, 6) };
            var start = ButtonOf("全部开始", 105, () => ResumeJobs(VisibleJobs().ToArray()), glyph: "play");
            var pause = ButtonOf("全部暂停", 105, () => PauseJobs(VisibleJobs().ToArray()), glyph: "pause");
            start.Dock = DockStyle.Left;
            pause.Dock = DockStyle.Right;
            actions.Controls.Add(start);
            actions.Controls.Add(pause);
            var queueHeading = new Panel { Dock = DockStyle.Fill, BackColor = Material.Card };
            queueHeading.Controls.Add(downloadQueueCount);
            queueHeading.Controls.Add(title);
            header.Controls.Add(queueHeading);
            header.Controls.Add(actions);
            header.SizeChanged += (s, e) =>
            {
                if (!layoutScaled) return;
                bool compact = header.ClientSize.Width < Ui(334);
                actions.Dock = compact ? DockStyle.Bottom : DockStyle.Right;
                if (compact)
                {
                    actions.Height = Ui(41);
                    actions.Padding = new Padding(Math.Max(0, header.ClientSize.Width - Ui(218)), 0, 0, Ui(6));
                }
                else
                {
                    actions.Width = Ui(218);
                    actions.Padding = new Padding(0, 0, 0, Ui(6));
                }
                header.Height = Ui(compact ? 78 : 41);
            };
            downloadQueueHost.Dock = DockStyle.Fill;
            downloadQueueHost.BackColor = Material.Card;
            card.Controls.Add(downloadQueueHost);
            card.Controls.Add(header);
            return card;
        }

        private void AttachTaskList()
        {
            bool downloading = page == DownloadPage;
            var host = downloading ? downloadQueueHost : taskListHost;
            if (list.Parent != host)
            {
                host.Controls.Add(list);
                host.Controls.Add(empty);
            }
            ApplyTaskView();
        }

        private void ConfigureTasks()
        {
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Material.Card;
            list.ForeColor = Material.Ink;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = true;
            list.ShowItemToolTips = true;
            list.OwnerDraw = true;
            list.AccessibleName = "媒体任务";
            rowHeight = new ImageList { ImageSize = new Size(1, Ui(49)) };
            rowHeight.Images.Add(new Bitmap(1, Ui(49)));
            list.SmallImageList = rowHeight;
            ConfigureCompletedCards();
            foreach (string title in new[] { "标题", "类型", "进度", "状态", "添加时间" }) list.Columns.Add(title);
            list.SizeChanged += (s, e) => ResizeColumns();
            list.DrawColumnHeader += DrawColumnHeader;
            list.DrawItem += PaintCompletedCard;
            list.DrawSubItem += DrawTaskCell;
            list.ItemSelectionChanged += (s, e) => list.Invalidate(e.Item.Bounds);
            list.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right) ShowMenu(TaskMenu(), list, e.Location);
                if (page != DownloadPage || e.Button != MouseButtons.Left || e.Clicks != 1) return;
                var row = list.GetItemAt(e.X, e.Y);
                var job = row == null ? null : row.Tag as MediaTask;
                if (job == null || !QueueActionBounds(row.Bounds).Contains(e.Location)) return;
                if (active.ContainsKey(job)) PauseJobs(new[] { job });
                else ResumeJobs(new[] { job });
            };
            empty.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowMenu(TaskMenu(), empty, e.Location); };
            list.ItemActivate += (s, e) =>
            {
                if (list.SelectedItems.Count > 0) OpenTaskRow(list.SelectedItems[0].Tag);
            };
            list.ColumnClick += (s, e) =>
            {
                jobs.Sort((a, b) =>
                {
                    switch (e.Column)
                    {
                        case 0: return String.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                        case 1: return String.Compare(a.Local ? a.Format : a.Kind, b.Local ? b.Format : b.Kind, StringComparison.CurrentCultureIgnoreCase);
                        case 2: return b.Percent.CompareTo(a.Percent);
                        case 3: return String.Compare(a.State, b.State, StringComparison.CurrentCulture);
                        default: return b.Added.CompareTo(a.Added);
                    }
                });
                Changed();
            };
            empty.Dock = DockStyle.Fill;
            empty.DoubleClick += (s, e) => { if (page == 2) ImportMedia(); else if (page == DownloadPage) PasteComposerInput(ClipboardText()); };
        }

        private void ResizeColumns()
        {
            if (list.View != View.Details) return;
            if (page == DownloadPage)
            {
                for (int i = 1; i < list.Columns.Count; i++)
                    if (list.Columns[i].Width != 0) list.Columns[i].Width = 0;
                int available = Math.Max(1, list.ClientSize.Width - 1);
                if (list.Columns[0].Width != available) list.Columns[0].Width = available;
                if (list.IsHandleCreated) ShowScrollBar(list.Handle, 0, false);
                return;
            }
            int width = Math.Max(Ui(540), list.ClientSize.Width);
            list.Columns[1].Width = Ui(70);
            list.Columns[2].Width = Ui(116);
            list.Columns[3].Width = width > Ui(750) ? Ui(164) : Ui(124);
            list.Columns[4].Width = Ui(108);
            list.Columns[0].Width = width - list.Columns[1].Width - list.Columns[2].Width - list.Columns[3].Width - list.Columns[4].Width;
        }

        private void DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var fill = new SolidBrush(Material.Container)) e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font,
                new Rectangle(e.Bounds.Left + 12, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height), Material.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawTaskCell(object sender, DrawListViewSubItemEventArgs e)
        {
            var folder = e.Item.Tag as CompletedFolder;
            var file = e.Item.Tag as CompletedFile;
            var job = file == null ? e.Item.Tag as MediaTask : file.Job;
            if (job == null && folder == null) return;
            if (page == DownloadPage)
            {
                if (e.ColumnIndex == 0) PaintQueueRow(e, job);
                return;
            }
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = e.Bounds;
            // The native first subitem can report the width of the full row.
            bounds.Width = Math.Min(bounds.Width, list.Columns[e.ColumnIndex].Width);
            using (var fill = new SolidBrush(e.Item.Selected ? Material.Accent : Material.Card)) g.FillRectangle(fill, bounds);
            using (var line = new Pen(Material.Line)) g.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            const TextFormatFlags text = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var content = new Rectangle(bounds.Left + 12, bounds.Top, bounds.Width - 19, bounds.Height);
            if (e.ColumnIndex == 0)
            {
                Material.Fill(g, new Rectangle(bounds.Left + 9, bounds.Top + (bounds.Height - 29) / 2, 29, 29),
                    9, e.Item.Selected ? Material.Card : Material.Container);
                Material.Icon(g, folder != null ? "folder" : job.Local ? "convert" : job.State == "已完成" ? "done" : "download",
                    new Rectangle(bounds.Left + 15, bounds.Top + (bounds.Height - 17) / 2, 17, 17), Material.Primary);
                content.X += 34; content.Width -= 34;
                TextRenderer.DrawText(g, e.SubItem.Text, Font, content, Material.Ink, text);
            }
            else if (folder != null) TextRenderer.DrawText(g, e.SubItem.Text, Font, content, Material.Muted, text);
            else if (e.ColumnIndex == 2)
            {
                double percent = Math.Max(0, Math.Min(100, job.Percent));
                TextRenderer.DrawText(g, e.SubItem.Text, Font, new Rectangle(content.X, bounds.Top + Ui(5), content.Width, Ui(22)), Material.Ink, text);
                var track = new RectangleF(content.X + 2, bounds.Top + Ui(33), Math.Max(1, content.Width - Ui(6)), Ui(4));
                Material.Fill(g, track, 2, Material.Line);
                track.Width = (float)(track.Width * percent / 100);
                Material.Fill(g, track, 2, job.State == "失败" ? Material.Error : Material.Primary);
            }
            else if (e.ColumnIndex == 3)
            {
                bool error = job.State == "失败", complete = job.State == "已完成";
                bool running = job.State == "解析中" || job.State == "下载中" || job.State == "转换中";
                Color fill = error ? Material.ErrorContainer : complete ? Material.SuccessContainer :
                    running && !e.Item.Selected ? Material.Accent : Material.Container;
                Color ink = error ? Material.Error : complete ? Material.Success : running ? Material.OnAccent : Material.Muted;
                int chipWidth = TextRenderer.MeasureText(job.State, Font).Width + 15;
                var chip = new Rectangle(content.X, bounds.Top + (bounds.Height - Ui(24)) / 2, chipWidth, Ui(24));
                Material.Fill(g, chip, 12, fill);
                TextRenderer.DrawText(g, job.State, Font, chip, ink, text | TextFormatFlags.HorizontalCenter);
                if (job.Speed > 0)
                    TextRenderer.DrawText(g, SizeText(job.Speed) + "/s", Font,
                        new Rectangle(chip.Right + 6, bounds.Top, Math.Max(1, content.Right - chip.Right - 6), bounds.Height), Material.Muted, text);
            }
            else TextRenderer.DrawText(g, e.SubItem.Text, Font, content, Material.Muted, text);
        }

        private static Rectangle QueueActionBounds(Rectangle row)
        {
            return new Rectangle(row.Left + 6, row.Top + (row.Height - 34) / 2, 34, 34);
        }

        private void PaintQueueRow(DrawListViewSubItemEventArgs e, MediaTask job)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = e.Bounds;
            using (var fill = new SolidBrush(e.Item.Selected ? Material.Accent : Material.Card)) g.FillRectangle(fill, bounds);
            using (var line = new Pen(Material.Line)) g.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);

            var action = QueueActionBounds(bounds);
            bool running = job.State == "下载中" || job.State == "解析中";
            Material.Fill(g, action, 9, Material.Container);
            Material.Icon(g, running ? "pause" : "play",
                new Rectangle(action.Left + 8, action.Top + 8, 18, 18), Material.Primary);

            int x = action.Right + 9;
            int width = Math.Max(1, bounds.Right - x - 7);
            double percent = Math.Max(0, Math.Min(100, job.Percent));
            string detail = job.State;
            if (job.Speed > 0) detail += "  ·  " + SizeText(job.Speed) + "/s";
            else if (job.State == "失败" && !String.IsNullOrWhiteSpace(job.Detail)) detail += "  ·  " + job.Detail.Split('\n')[0].Trim();
            const TextFormatFlags text = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, job.Title ?? job.Source, Font, new Rectangle(x, bounds.Top + Ui(3), width, Ui(19)), Material.Ink, text);
            TextRenderer.DrawText(g, detail, Font, new Rectangle(x, bounds.Top + Ui(21), Math.Max(1, width - Ui(49)), Ui(17)),
                job.State == "失败" ? Material.Error : Material.Muted, text);
            TextRenderer.DrawText(g, percent.ToString("0.#") + "%", Font, new Rectangle(x, bounds.Top + Ui(21), width, Ui(17)),
                Material.Primary, text | TextFormatFlags.Right);
            var track = new RectangleF(x, bounds.Bottom - 7, width, 4);
            Material.Fill(g, track, 2, Material.Container);
            track.Width *= (float)(percent / 100);
            Material.Fill(g, track, 2, job.State == "失败" ? Material.Error : Material.Primary);
        }

        private void UpdateDashboard(MediaTask[] shown, int rowCount)
        {
            if (page == DownloadPage)
            {
                int downloading = shown.Count(j => j.State == "下载中" || j.State == "解析中");
                downloadQueueCount.Text = shown.Length + " 项";
                downloadStatus.Text = downloading + " 项下载中";
                count.Text = "";
                status.Text = "输出目录  ·  " + options.Downloads;
                list.Visible = shown.Length > 0;
                empty.Visible = shown.Length == 0;
                if (list.Visible) ResizeColumns();
                empty.ShowMessage("download", "下载队列为空", "从左侧解析结果加入下载队列");
                return;
            }
            bool converting = page == 2;
            string title = converting ? "转换队列为空" : filter.Length == 0 ? "暂无已完成任务" : "没有匹配的任务";
            string hint = converting ? "导入或拖入媒体文件，选择格式后开始转换" :
                filter.Length == 0 ? "下载完成的文件会显示在这里" : "试试其他关键词";
            empty.ShowMessage(PageIcons[page], title, hint);
            list.Visible = rowCount > 0;
            empty.Visible = rowCount == 0;
            count.Text = rowCount + " 项";
            int running = shown.Count(j => active.ContainsKey(j) && j.State != "已暂停");
            int failed = shown.Count(j => j.State == "失败");
            int complete = shown.Count(j => j.State == "已完成");
            status.Text = "输出目录  ·  " + (page == 2 ? options.Converted : completedFolder ?? options.Downloads);
            string failure = failed > 0 ? "  ·  " + failed + " 项失败" : "";
            activity.Text = converting ? running + " 项运行中    ·    " + complete + " 项完成" + failure :
                shown.Sum(j => j.Files != null ? j.Files.Length : j.File == null ? 0 : 1) + " 个文件";
        }
    }
}
