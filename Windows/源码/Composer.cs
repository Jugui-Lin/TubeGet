using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private const int DownloadPage = 0;
        private readonly Panel downloadOptionsPanel = new Panel();
        private readonly Panel downloadQueueHost = new Panel();
        private const int AutoLink = 0, SingleLink = 1, CollectionLinks = 2, TextLinks = 3, Sidecars = 4;
        private readonly Panel composer = new Panel();
        private readonly BufferedTaskList choices = new BufferedTaskList();
        private readonly PictureBox preview = new PictureBox();
        private readonly Label summary = new Label();
        private readonly Label mediaTitle = new Label();
        private readonly Panel mediaDetails = new Panel();
        private MaterialCard composerResultsCard;
        private Label resultTitle;
        private Font formatTitleFont;
        private int formatSizeColumnWidth;
        private bool resizingComposerColumns;
        private ImageList compactRows, formatRows;
        private readonly Label choiceStatus = new Label();
        private readonly Label emptyResults = new Label();
        private readonly Label resultCount = new Label();
        private Button pasteComposerButton, clearListButton, copyLinkButton, reanalyzeButton;
        private readonly Panel composerTableHeader = new Panel();
        private readonly CheckBox selectAllCheck = new ComposerSelectionBox();
        private readonly TextBox links = new TextBox();
        private readonly TextBox destination = new TextBox();
        private SettingPicker modePicker, audioPicker, subtitlePicker, qualityPicker, subtitleFormat;
        private SettingToggle coverPicker, numberPicker;
        private Button addButton, importButton, clearComposerButton;
        private CancellationTokenSource analysis;
        private int composerMode;
        private int resultMode;
        private bool updatingRows, clearingComposerInput;
        private string clearedComposerInput;
        private int analysisGeneration;
        private string composerCookies, composerCookieBrowser, composerPassword, analyzedUrl, collectionDirectoryName, collectionCoverUrl;
        private string[] audioIds = new string[] { null };
        private string[] subtitleIds = new string[] { null };
        private bool[] automaticSubtitles = new[] { false };

        private sealed class Entry
        {
            internal string Url, Title, Format, FormatDetail, Thumbnail;
            internal bool HasVideo, NeedsAudio;
        }

        private void OpenComposer(int mode = AutoLink, string input = null)
        {
            SetPage(DownloadPage);
            if (mode != composerMode)
                modePicker.Choose(mode);
            if (!String.IsNullOrEmpty(input)) SetComposerInput(input);
            links.Focus();
        }

        private static readonly string[] ComposerModes = { "自动识别", "单个链接", "列表解析", "链接列表", "字幕与封面" };
        private static readonly string[] BatchQualities = { "默认画质", "2160", "1440", "1080", "720", "480", "360" };

        private MaterialCard BuildComposerInput()
        {
            var top = new MaterialCard { Dock = DockStyle.Top, Height = 136, Radius = 18, Padding = new Padding(13, 9, 13, 7), BackColor = Material.Card, FillColor = Material.LinkCard };
            var modeRow = new FlowLayoutPanel {
                Dock = DockStyle.Top, Height = 32, WrapContents = false, BackColor = Material.LinkCard, TabIndex = 0
            };
            modeRow.Controls.Add(new Label {
                Text = "视频链接", Width = 73, Height = 28, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Material.Ink
            });
            modePicker = new SettingPicker(ComposerModes, ComposerModes[0]) {
                Width = 113, Height = 29, Margin = Padding.Empty, AccessibleName = "任务类型"
            };
            modeRow.Controls.Add(modePicker);

            var tools = new FlowLayoutPanel {
                Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 3, 0, 0),
                BackColor = Material.LinkCard, TabIndex = 2
            };
            importButton = ButtonOf("导入 TXT", 96, ImportLinkFile, null, "folder", ButtonStyle.Quiet);
            tools.Controls.Add(importButton);
            tools.Controls.Add(ButtonOf("登录选项", 100, EditComposerAccess, null, "settings", ButtonStyle.Quiet));

            pasteComposerButton = ButtonOf("粘贴链接", 119, () => PasteComposerInput(ClipboardText()),
                "粘贴链接并解析", "clipboard", ButtonStyle.Filled);
            clearComposerButton = ButtonOf("清空", 52, ClearComposerInput,
                "清空链接输入框", null, ButtonStyle.Quiet);
            clearComposerButton.Enabled = false;
            links.Multiline = true;
            links.ScrollBars = ScrollBars.None;
            links.Dock = DockStyle.Fill;
            links.AccessibleName = "视频或列表链接";
            Material.Field(links);
            links.BackColor = Material.Card;
            links.BorderStyle = BorderStyle.None;
            links.TextChanged += (s, e) =>
            {
                clearComposerButton.Enabled = links.TextLength > 0;
                if (clearingComposerInput) return;
                composerPassword = null;
                ResetComposerResult();
            };
            links.KeyDown += (s, e) =>
            {
                if (!e.Control && e.KeyCode == Keys.Enter && composerMode != TextLinks)
                {
                    e.SuppressKeyPress = true;
                    AnalyzeComposer();
                }
            };
            var inputRegion = new Panel { Dock = DockStyle.Top, Height = 49, BackColor = Material.LinkCard, TabIndex = 1 };
            var inputWell = new MaterialCard {
                Dock = DockStyle.Fill, Radius = 9, BackColor = Material.LinkCard, FillColor = Material.Card,
                Padding = new Padding(10, 4, 4, 4), TabIndex = 0
            };
            var linkField = new Panel { Dock = DockStyle.Fill, BackColor = Material.Card, Padding = new Padding(0, 4, 3, 0) };
            linkField.Controls.Add(links);
            var clearSlot = new Panel { Dock = DockStyle.Right, Width = clearComposerButton.Width, BackColor = Material.Card, TabIndex = 1 };
            clearComposerButton.Dock = DockStyle.Fill;
            clearSlot.Controls.Add(clearComposerButton);
            inputWell.Controls.Add(linkField);
            inputWell.Controls.Add(clearSlot);

            var actions = new FlowLayoutPanel {
                Dock = DockStyle.Right, Width = pasteComposerButton.Width + 7,
                Padding = new Padding(7, 5, 0, 0), WrapContents = false, BackColor = Material.LinkCard, TabIndex = 1
            };
            pasteComposerButton.Margin = Padding.Empty;
            actions.Controls.Add(pasteComposerButton);
            inputRegion.Controls.Add(inputWell);
            inputRegion.Controls.Add(actions);
            top.Controls.Add(tools);
            top.Controls.Add(inputRegion);
            top.Controls.Add(modeRow);
            return top;
        }

        private void BuildComposer()
        {
            composer.Dock = DockStyle.Fill;
            composer.BackColor = Material.Card;
            composer.Visible = false;
            var top = BuildComposerInput();
            var bottomContent = new Panel {
                Dock = DockStyle.Bottom, Height = 47, Padding = new Padding(0, 8, 0, 0), BackColor = Material.Card
            };
            bottomContent.Paint += (s, e) =>
            {
                using (var pen = new Pen(Material.Line)) e.Graphics.DrawLine(pen, 0, 1, bottomContent.Width, 1);
            };
            choiceStatus.Dock = DockStyle.Fill;
            choiceStatus.ForeColor = Material.Muted;
            choiceStatus.AutoEllipsis = true;
            choiceStatus.TextChanged += (s, e) => tips.SetToolTip(choiceStatus, choiceStatus.Text);
            choiceStatus.TextAlign = ContentAlignment.MiddleLeft;
            addButton = ButtonOf("加入下载队列", 135, QueueComposer, null, "download", ButtonStyle.Filled);
            addButton.Dock = DockStyle.Right;
            reanalyzeButton = ButtonOf("重新解析", 83, () =>
            {
                string retryInput = clearedComposerInput;
                bool importedList = resultMode == TextLinks && composerMode == AutoLink;
                CancelComposerAnalysis();
                AnalyzeComposer(importedList, retryInput);
            }, null, null, ButtonStyle.Outlined);
            reanalyzeButton.Dock = DockStyle.Right;
            var logButton = ButtonOf("运行日志", 91, ShowRunLog, null, "queue", ButtonStyle.Tonal);
            logButton.Dock = DockStyle.Left;
            choiceStatus.Padding = new Padding(7, 0, 5, 0);
            bottomContent.Controls.Add(choiceStatus);
            bottomContent.Controls.Add(logButton);
            bottomContent.Controls.Add(reanalyzeButton);
            bottomContent.Controls.Add(addButton);
            var results = new TableLayoutPanel {
                Dock = DockStyle.Fill, BackColor = Material.Card, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0, 10, 0, 0)
            };
            results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var output = BuildComposerDestination();
            BuildComposerOptions();
            var right = BuildDownloadQueue();

            var left = composerResultsCard = new MaterialCard {
                Dock = DockStyle.Fill, Radius = 14, BackColor = Material.Card, Margin = new Padding(0, 0, 5, 0), Padding = new Padding(12, 8, 12, 10)
            };
            var resultsHeader = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Material.Card };
            resultTitle = ComposerSectionTitle("解析结果");
            resultTitle.Dock = DockStyle.Left;
            resultTitle.Width = 78;
            resultCount.Dock = DockStyle.Fill;
            resultCount.ForeColor = Material.Muted;
            resultCount.TextAlign = ContentAlignment.MiddleLeft;
            resultsHeader.Controls.Add(resultCount);
            resultsHeader.Controls.Add(resultTitle);
            clearListButton = ButtonOf("清空列表", 94,
                () => ResetComposerResult("已清空列表。按 Ctrl+Enter 可重新解析。"),
                "清空解析结果", null, ButtonStyle.Outlined);
            var listActions = new Panel {
                Dock = DockStyle.Right, Width = 182, BackColor = Material.Card, Padding = new Padding(0, 0, 0, 6)
            };
            clearListButton.Dock = DockStyle.Right;
            copyLinkButton = ButtonOf("复制链接", 82, () =>
            {
                if (analyzedUrl == null) return;
                if (Copy(analyzedUrl)) choiceStatus.Text = "已复制作品链接";
            }, null, null, ButtonStyle.Quiet);
            copyLinkButton.Dock = DockStyle.Right;
            listActions.Controls.Add(copyLinkButton);
            listActions.Controls.Add(clearListButton);
            copyLinkButton.VisibleChanged += (s, e) => listActions.Width = copyLinkButton.Visible ? (layoutScaled ? Ui(182) : 182) : clearListButton.Width;
            resultsHeader.Controls.Add(listActions);

            composerTableHeader.Dock = DockStyle.Top;
            composerTableHeader.Height = 27;
            composerTableHeader.BackColor = Material.Container;
            composerTableHeader.Paint += PaintComposerHeader;
            selectAllCheck.Dock = DockStyle.Left;
            selectAllCheck.Width = 25;
            selectAllCheck.Click += (s, e) => SetAllComposer(selectAllCheck.CheckState != CheckState.Checked);
            tips.SetToolTip(selectAllCheck, "全选／取消全选");
            composerTableHeader.Controls.Add(selectAllCheck);
            choices.Dock = DockStyle.Fill;
            choices.View = View.Details;
            choices.FullRowSelect = true;
            choices.HideSelection = false;
            choices.ShowItemToolTips = true;
            choices.BackColor = Material.Card;
            choices.OwnerDraw = true;
            choices.HeaderStyle = ColumnHeaderStyle.None;
            choices.DrawSubItem += PaintComposerRow;
            choices.ItemSelectionChanged += (s, e) => choices.Invalidate(e.Item.Bounds);
            choices.ForeColor = Material.Ink;
            choices.BorderStyle = BorderStyle.None;
            choices.AccessibleName = "解析结果";
            choices.Columns.Add("画质或标题", 200);
            choices.Columns.Add("编码 / 来源", 100);
            choices.Columns.Add("大小 / 时长", 76);
            compactRows = CreateListSpacing(1, Ui(30));
            formatRows = CreateListSpacing(1, Ui(46));
            choices.SmallImageList = compactRows;
            formatTitleFont = new Font(Font, FontStyle.Bold);
            choices.Disposed += (s, e) => { compactRows.Dispose(); formatRows.Dispose(); formatTitleFont.Dispose(); };
            choices.SizeChanged += (s, e) => ResizeComposerColumns();
            choices.ItemChecked += (s, e) =>
            {
                if (!updatingRows) choices.Invalidate(e.Item.Bounds);
                UpdateComposerSelection();
            };
            choices.SelectedIndexChanged += (s, e) =>
            {
                if (updatingRows) return;
                UpdateComposerSelection();
                if (resultMode == SingleLink && choices.SelectedItems.Count > 0)
                    choiceStatus.Text = SelectedFormatStatus();
            };
            choices.SelectedIndexChanged += SelectSidecarLanguage;
            emptyResults.Dock = DockStyle.Fill;
            emptyResults.BackColor = Material.Container;
            emptyResults.ForeColor = Material.Muted;
            emptyResults.TextAlign = ContentAlignment.MiddleCenter;
            emptyResults.Text = "解析后可在这里选择画质、字幕或列表条目";
            left.Controls.Add(choices);
            left.Controls.Add(emptyResults);
            left.Controls.Add(bottomContent);
            left.Controls.Add(composerTableHeader);
            left.Controls.Add(BuildComposerMediaDetails());
            left.Controls.Add(resultsHeader);
            results.Controls.Add(left, 0, 0);
            results.Controls.Add(right, 1, 0);
            composer.Controls.Add(results);
            composer.Controls.Add(output);
            composer.Controls.Add(top);
            modePicker.SelectionChanged += (s, e) => SetComposerMode(modePicker.SelectedIndex);
            subtitlePicker.SelectionChanged += (s, e) => UpdateComposerSelection();
            coverPicker.CheckedChanged += (s, e) => UpdateComposerSelection();
            SetComposerMode(AutoLink);
            composer.Disposed += (s, e) =>
            {
                CancelComposerAnalysis();
                if (preview.Image != null) preview.Image.Dispose();
                ClearCollectionPreviews();
                downloadOptionsPanel.Dispose();
            };
        }

        private Control BuildComposerDestination()
        {
            var host = new Panel { Dock = DockStyle.Top, Height = 63, Padding = new Padding(0, 10, 0, 0), BackColor = Material.Card };
            var card = new MaterialCard { Dock = DockStyle.Fill, Radius = 15, BackColor = Material.Card, Padding = new Padding(12, 8, 10, 8) };
            var row = new Panel { Dock = DockStyle.Fill, BackColor = Material.Card };
            var caption = new Label {
                Text = "保存到", Dock = DockStyle.Left, Width = 80, TextAlign = ContentAlignment.MiddleRight, ForeColor = Material.Ink,
                Padding = new Padding(0, 0, 10, 0)
            };
            caption.Paint += (s, e) => Material.Icon(e.Graphics, "folder", new Rectangle(1, (caption.Height - 19) / 2, 19, 19), Material.Primary);
            destination.Text = options.Downloads;
            destination.AccessibleName = "本次下载目录";
            var field = new SettingField(destination) { Dock = DockStyle.Fill };
            var browse = ButtonOf("浏览", 66, () =>
            {
                using (var dialog = new FolderBrowserDialog { SelectedPath = destination.Text, Description = "本次下载目录" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
            }, null, null, ButtonStyle.Outlined);
            var settings = ButtonOf("附加选项", 99, ShowComposerOptions, "音轨、字幕与封面", null, ButtonStyle.Outlined);
            browse.Dock = settings.Dock = DockStyle.Right;
            row.Controls.Add(field);
            row.Controls.Add(caption);
            row.Controls.Add(browse);
            row.Controls.Add(settings);
            card.Controls.Add(row);
            host.Controls.Add(card);
            return host;
        }

        private void BuildComposerOptions()
        {
            downloadOptionsPanel.Dock = DockStyle.Fill;
            downloadOptionsPanel.AutoScroll = true;
            downloadOptionsPanel.BackColor = Material.Card;
            var settings = new TableLayoutPanel {
                Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Material.Card, Padding = new Padding(12, 8, 12, 8)
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            audioPicker = new SettingPicker(new[] { "自动选择" }, "自动选择") { AccessibleName = "音轨" };
            AddComposerOption(settings, "音轨", audioPicker);
            qualityPicker = new SettingPicker(BatchQualities, BatchQualities[0]) { AccessibleName = "清晰度上限" };
            AddComposerOption(settings, "清晰度上限", qualityPicker);
            subtitlePicker = new SettingPicker(new[] { "默认语言" }, "默认语言") { AccessibleName = "字幕语言" };
            AddComposerOption(settings, "字幕语言", subtitlePicker);
            subtitleFormat = new SettingPicker(new[] { "默认格式", "srt", "vtt", "ass", "原始" }, "默认格式") { AccessibleName = "字幕格式" };
            AddComposerOption(settings, "字幕格式", subtitleFormat);
            coverPicker = new SettingToggle { Text = "同时保存封面", Checked = options.Cover };
            AddComposerRow(settings, coverPicker, 32);
            numberPicker = new SettingToggle { Text = "添加编号" };
            tips.SetToolTip(numberPicker, "添加 001、002… 编号");
            AddComposerRow(settings, numberPicker, 32);
            downloadOptionsPanel.Controls.Add(settings);
        }

        private static ImageList CreateListSpacing(int width, int height)
        {
            var spacing = new ImageList { ImageSize = new Size(Math.Min(256, width), Math.Min(256, height)) };
            using (var blank = new Bitmap(spacing.ImageSize.Width, spacing.ImageSize.Height))
            {
                spacing.Images.Add(blank, Color.Transparent);
                // Build the native image list before releasing the row-height bitmap.
                if (spacing.Handle == IntPtr.Zero) throw new InvalidOperationException("无法初始化画质列表。");
            }
            return spacing;
        }

        private Control BuildComposerMediaDetails()
        {
            mediaDetails.Dock = DockStyle.Top;
            mediaDetails.Height = 70;
            mediaDetails.Padding = new Padding(0, 1, 0, 8);
            mediaDetails.BackColor = Material.Card;
            preview.Dock = DockStyle.Left;
            preview.Width = 105;
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            preview.BackColor = Material.Container;
            preview.AccessibleName = "作品封面";
            preview.Paint += (s, e) =>
            {
                if (preview.Image != null) return;
                int size = Ui(23);
                Material.Icon(e.Graphics, "play", new Rectangle((preview.Width - size) / 2,
                    (preview.Height - size) / 2, size, size), Material.Muted);
            };
            var details = new Panel { Dock = DockStyle.Fill, Padding = new Padding(9, 2, 0, 1), BackColor = Material.Card };
            mediaTitle.Dock = DockStyle.Top;
            mediaTitle.Height = 33;
            mediaTitle.AutoEllipsis = true;
            var titleFont = UiFont(12, FontStyle.Bold);
            mediaTitle.Font = titleFont;
            mediaTitle.Disposed += (s, e) => titleFont.Dispose();
            mediaTitle.ForeColor = Material.Ink;
            summary.Dock = DockStyle.Fill;
            summary.AutoEllipsis = true;
            summary.ForeColor = Material.Muted;
            var metadataFont = UiFont(9);
            summary.Font = metadataFont;
            summary.Disposed += (s, e) => metadataFont.Dispose();
            mediaTitle.TextChanged += (s, e) => tips.SetToolTip(mediaTitle, mediaTitle.Text);
            summary.TextChanged += (s, e) => tips.SetToolTip(summary, summary.Text);
            details.Controls.Add(summary);
            details.Controls.Add(mediaTitle);
            mediaDetails.Controls.Add(details);
            mediaDetails.Controls.Add(preview);
            return mediaDetails;
        }

        private void ShowComposerOptions()
        {
            using (var dialog = Dialog("下载选项", 500, 420, new FirstPaintWindow()))
            {
                var footer = Footer();
                footer.Controls.Add(ButtonOf("完成", 82, () => dialog.DialogResult = DialogResult.OK, null, null, ButtonStyle.Filled));
                dialog.Controls.Add(footer);
                dialog.Load += (s, e) => dialog.Controls.Add(downloadOptionsPanel);
                try { ShowAppDialog(dialog, this); }
                finally { dialog.Controls.Remove(downloadOptionsPanel); }
            }
        }

        private Label ComposerSectionTitle(string text)
        {
            return new Label {
                Text = text, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Material.Ink,
                Font = UiFont(12, FontStyle.Bold)
            };
        }

        private static void AddComposerRow(TableLayoutPanel table, Control item, int height)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            item.Dock = DockStyle.Fill;
            table.Controls.Add(item, 0, row);
        }

        private void AddComposerOption(TableLayoutPanel table, string label, Control control)
        {
            var row = new Panel { Padding = new Padding(0, 2, 0, 1) };
            var caption = new Label { Text = label, Width = 83, Dock = DockStyle.Left, ForeColor = Material.Muted, TextAlign = ContentAlignment.MiddleLeft };
            control.Dock = DockStyle.Fill;
            row.Controls.Add(control);
            row.Controls.Add(caption);
            AddComposerRow(table, row, 33);
        }

        private void SetComposerMode(int mode)
        {
            if (composerMode != mode) composerPassword = null;
            composerMode = mode;
            importButton.Enabled = mode == AutoLink || mode == TextLinks;
            ResetComposerResult();
        }

        private void ConfigureComposerResult(int mode)
        {
            resultMode = mode;
            bool batch = mode == CollectionLinks || mode == TextLinks;
            choices.CheckBoxes = batch;
            choices.MultiSelect = batch;
            choices.CheckboxClickWidth = batch || mode == SingleLink ? Ui(25) : 0;
            selectAllCheck.Visible = batch;
            composerTableHeader.Invalidate();
            SetComposerRowHeight(numberPicker, batch ? 32 : 0);
            var spacing = mode == SingleLink || mode == CollectionLinks ? formatRows : compactRows;
            if (choices.SmallImageList != spacing) choices.SmallImageList = spacing;
            SetComposerRowHeight(qualityPicker.Parent, batch ? 33 : 0);
            SetComposerRowHeight(audioPicker.Parent, !batch && mode != Sidecars ? 33 : 0);
            coverPicker.Text = mode == Sidecars ? "保存封面" : "同时保存封面";
            choices.Columns[0].Text = mode == Sidecars ? "字幕语言" : batch ? "标题" : "画质";
            choices.Columns[1].Text = mode == Sidecars ? "字幕类型" : batch ? "来源" : "编码";
            choices.Columns[2].Text = batch ? "时长" : mode == Sidecars ? "" : "大小";
        }

        private void ClearComposerInput()
        {
            clearedComposerInput = links.Text;
            clearingComposerInput = true;
            try { links.Clear(); }
            finally { clearingComposerInput = false; }
            links.Focus();
        }

        private void SetComposerInput(string input)
        {
            links.Text = input.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }

        private void PasteComposerInput(string input)
        {
            if (!Urls(input).Any())
            {
                choiceStatus.Text = "剪贴板中没有可解析的链接";
                return;
            }
            SetComposerInput(input);
            if (analysis == null) AnalyzeComposer();
        }

        private void SetComposerRowHeight(Control row, int height)
        {
            var table = (TableLayoutPanel)row.Parent;
            table.RowStyles[table.GetRow(row)].Height = layoutScaled ? Ui(height) : height;
            row.Visible = height > 0;
        }

        private void CancelComposerAnalysis()
        {
            analysisGeneration++;
            var pending = analysis;
            if (pending == null) return;
            analysis = null;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { pending.Cancel(); } catch (ObjectDisposedException) { }
            });
        }

        private void ResetComposerResult(string status = null)
        {
            CancelComposerAnalysis();
            PagePainting.Update(composerResultsCard, () =>
            {
                updatingRows = true;
                try
                {
                    // Hide the native list before changing its rows and checkbox style.
                    choices.Hide();
                    clearedComposerInput = null;
                    analyzedUrl = collectionDirectoryName = collectionCoverUrl = null;
                    formatSizeColumnWidth = 0;
                    ClearCollectionPreviews();
                    choices.Items.Clear();
                    if (preview.Image != null) { var old = preview.Image; preview.Image = null; old.Dispose(); }
                    ConfigureComposerResult(composerMode == AutoLink ? SingleLink : composerMode);
                    mediaTitle.Text = summary.Text = "";
                    audioIds = new string[] { null };
                    subtitleIds = new string[] { null };
                    automaticSubtitles = new[] { false };
                    audioPicker.SetChoices(new[] { "自动选择" }, "自动选择");
                    string defaultLabel = composerMode == Sidecars ? "不下载字幕" : "默认语言";
                    subtitlePicker.SetChoices(new[] { defaultLabel }, defaultLabel);
                }
                finally { updatingRows = false; }

                UpdateComposerSelection();
                choiceStatus.Text = status ?? (composerMode == AutoLink ? "点「粘贴链接」自动解析" :
                    composerMode == TextLinks ? "粘贴多个链接，或导入 TXT 文件" :
                    composerMode == CollectionLinks ? "输入作者主页、频道、合集或播放列表链接" : "输入链接后按回车解析");
            });
        }

        private void ImportLinkFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "文本文件|*.txt|所有文件|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string[] imported = Urls(File.ReadAllText(dialog.FileName)).Distinct().ToArray();
                    if (imported.Length == 0) { choiceStatus.Text = "TXT 文件里没有有效链接"; return; }
                    SetComposerInput(String.Join(Environment.NewLine, imported));
                    AnalyzeComposer(true);
                }
                catch (Exception ex) { choiceStatus.Text = ex.Message; }
            }
        }

        private void SetAllComposer(bool check)
        {
            choices.BeginUpdate();
            updatingRows = true;
            try { foreach (ListViewItem row in choices.Items) row.Checked = check; }
            finally { updatingRows = false; choices.EndUpdate(); }
            choices.Invalidate();
            UpdateComposerSelection();
        }

        private void UpdateComposerSelection()
        {
            if (updatingRows) return;
            bool hasRows = choices.Items.Count > 0;
            choices.Visible = hasRows;
            if (hasRows) ResizeComposerColumns();
            composerTableHeader.Visible = hasRows;
            clearListButton.Enabled = analysis != null || analyzedUrl != null || choices.Items.Count > 0;
            bool batch = resultMode == CollectionLinks || resultMode == TextLinks;
            int selected = batch ? choices.CheckedItems.Count : choices.SelectedItems.Count;
            selectAllCheck.CheckState = selected == 0 ? CheckState.Unchecked :
                selected == choices.Items.Count ? CheckState.Checked : CheckState.Indeterminate;
            bool sidecars = resultMode == Sidecars;
            bool hasDetails = resultMode != TextLinks && analyzedUrl != null;
            mediaDetails.Visible = preview.Visible = copyLinkButton.Visible = reanalyzeButton.Visible = hasDetails;
            resultTitle.Text = resultMode == SingleLink && hasDetails ? "选择画质" : "解析结果";
            var entry = selected == 0 || batch ? null : choices.SelectedItems[0].Tag as Entry;
            audioPicker.Enabled = entry != null && entry.NeedsAudio && audioIds.Length > 1;
            addButton.Enabled = analyzedUrl != null && (sidecars ? coverPicker.Checked || subtitlePicker.SelectedIndex > 0 : selected > 0);
            resultCount.Text = batch && choices.Items.Count > 0 ? choices.Items.Count + " 项" : "";
            if (analyzedUrl != null && batch) choiceStatus.Text = choices.Items.Count == 0 ? "没有可用的视频条目" :
                "已选择 " + selected + " / " + choices.Items.Count + " 项" +
                (collectionDirectoryName == null ? "" : "  ·  " + collectionDirectoryName);
            emptyResults.Visible = !hasRows;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

        private void ResizeComposerColumns()
        {
            if (updatingRows || resizingComposerColumns || choices.IsDisposed || choices.Columns.Count < 3) return;
            resizingComposerColumns = true;
            try { ApplyComposerColumnWidths(); }
            finally { resizingComposerColumns = false; }
        }

        private void ApplyComposerColumnWidths()
        {
            int width = choices.ClientSize.Width;
            int source = resultMode == SingleLink
                ? Math.Max(Ui(70), TextRenderer.MeasureText("H.264", formatTitleFont).Width + Ui(20))
                : Math.Min(Ui(120), Math.Max(Ui(77), width / 5));
            int size = resultMode == Sidecars ? 0 : TextRenderer.MeasureText(
                resultMode == SingleLink ? "约 99.9 MB" : "999:59:59", Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Ui(22);
            if (resultMode == SingleLink) size = Math.Max(size, formatSizeColumnWidth);
            int title = Math.Max(1, width - source - size - 1);
            if (choices.Columns[0].Width > title) choices.Columns[0].Width = title;
            if (choices.Columns[1].Width != source) choices.Columns[1].Width = source;
            if (choices.Columns[2].Width != size) choices.Columns[2].Width = size;
            if (choices.Columns[0].Width < title) choices.Columns[0].Width = title;
            const int horizontalBar = 0;
            if (choices.IsHandleCreated && title + source + choices.Columns[2].Width <= width)
                ShowScrollBar(choices.Handle, horizontalBar, false);
            composerTableHeader.Invalidate();
        }

        private string SelectedFormatStatus()
        {
            return "已选 " + choices.SelectedItems[0].Text.Split('·')[0].TrimEnd();
        }

        private void PaintComposerHeader(object sender, PaintEventArgs e)
        {
            int left = 0;
            foreach (ColumnHeader column in choices.Columns)
            {
                int inset = left == 0 && (choices.CheckBoxes || resultMode == SingleLink) ? selectAllCheck.Width : 10;
                if (left == 0 && resultMode == CollectionLinks) inset = Ui(101);
                TextRenderer.DrawText(e.Graphics, column.Text, Font,
                    new Rectangle(left + inset, 0, Math.Max(1, column.Width - inset - 4), composerTableHeader.Height),
                    Material.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                left += column.Width;
            }
        }

        private void PaintComposerRow(object sender, DrawListViewSubItemEventArgs e)
        {
            var r = e.Bounds;
            r.Width = Math.Min(r.Width, choices.Columns[e.ColumnIndex].Width);
            if (resultMode == SingleLink)
            {
                PaintFormatRow(e, r);
                return;
            }
            using (var background = new SolidBrush(e.Item.Selected ? Material.Accent : Material.Card)) e.Graphics.FillRectangle(background, r);
            using (var border = new Pen(Material.Line)) e.Graphics.DrawLine(border, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            int inset = 10;
            if (e.ColumnIndex == 0 && choices.CheckBoxes)
            {
                var box = Material.ComposerCheckBounds(r);
                Material.DrawComposerCheck(e.Graphics, box, e.Item.Checked ? CheckState.Checked : CheckState.Unchecked, choices.Enabled);
                inset = Ui(25);
                if (resultMode == CollectionLinks)
                {
                    PaintCollectionPreview(e.Graphics, r, e.Item.Tag as Entry);
                    inset = Ui(101);
                }
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font,
                new Rectangle(r.Left + inset, r.Top, Math.Max(1, r.Width - inset - 4), r.Height),
                e.Item.Selected ? Material.OnAccent : Material.Ink, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void PaintFormatRow(DrawListViewSubItemEventArgs e, Rectangle cell)
        {
            var g = e.Graphics;
            bool selected = e.Item.Selected;
            Color fill = selected ? Material.SuccessContainer : Material.Container;
            Color stroke = selected ? Material.Positive : Material.Line;
            var band = new Rectangle(cell.X, cell.Y + 2, cell.Width, cell.Height - 5);
            using (var background = new SolidBrush(fill)) g.FillRectangle(background, band);
            using (var outline = new Pen(stroke))
            {
                g.DrawLine(outline, band.Left, band.Top, band.Right - 1, band.Top);
                g.DrawLine(outline, band.Left, band.Bottom - 1, band.Right - 1, band.Bottom - 1);
                if (e.ColumnIndex == 0) g.DrawLine(outline, band.Left, band.Top, band.Left, band.Bottom - 1);
                if (e.ColumnIndex == 2) g.DrawLine(outline, band.Right - 1, band.Top, band.Right - 1, band.Bottom - 1);
            }
            const TextFormatFlags text = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (e.ColumnIndex == 0)
            {
                var box = Material.ComposerCheckBounds(band);
                Material.DrawComposerCheck(g, box, selected ? CheckState.Checked : CheckState.Unchecked, choices.Enabled);
                int x = band.Left + Ui(28);
                int width = Math.Max(1, band.Right - x - 3);
                TextRenderer.DrawText(g, e.SubItem.Text, formatTitleFont,
                    new Rectangle(x, band.Top + Ui(2), width, Ui(19)), Material.Ink, text);
                var entry = (Entry)e.Item.Tag;
                TextRenderer.DrawText(g, entry.FormatDetail, Font,
                    new Rectangle(x, band.Top + Ui(21), width, Ui(16)), Material.Muted, text);
            }
            else if (e.ColumnIndex == 1)
            {
                string codec = e.SubItem.Text;
                int width = Math.Min(band.Width - 12, TextRenderer.MeasureText(codec, formatTitleFont).Width + 12);
                var badge = new Rectangle(band.Left + Ui(8), band.Top + (band.Height - Ui(20)) / 2, Math.Max(1, width), Ui(20));
                Color badgeFill = Material.Accent;
                Color badgeInk = Material.OnAccent;
                switch (codec)
                {
                    case "H.264":
                        badgeFill = Material.Positive;
                        badgeInk = Material.OnPositive;
                        break;
                    case "VP9":
                        badgeFill = Material.Dark ? Color.FromArgb(39, 63, 89) : Color.FromArgb(219, 234, 252);
                        badgeInk = Material.Dark ? Color.FromArgb(183, 218, 255) : Color.FromArgb(38, 75, 121);
                        break;
                    case "AV1":
                        badgeFill = Material.Dark ? Color.FromArgb(61, 45, 83) : Color.FromArgb(237, 226, 251);
                        badgeInk = Material.Dark ? Color.FromArgb(228, 206, 255) : Color.FromArgb(90, 54, 131);
                        break;
                }
                Material.Fill(g, badge, 5, badgeFill);
                TextRenderer.DrawText(g, codec, formatTitleFont, badge, badgeInk, text | TextFormatFlags.HorizontalCenter);
            }
            else TextRenderer.DrawText(g, e.SubItem.Text, Font,
                new Rectangle(band.Left, band.Top, Math.Max(1, band.Width - Ui(8)), band.Height),
                Material.Ink, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        private void EditComposerAccess()
        {
            using (var dialog = Dialog("登录选项", 555, 325, new FirstPaintWindow()))
            {
                var group = new PreferenceCard("登录与视频密码");
                var sources = new[] { "使用全局设置" }.Concat(CookieSessions.Labels).ToArray();
                var source = new SettingPicker(sources, composerCookies == null && composerCookieBrowser == null
                    ? sources[0] : CookieSessions.Label(composerCookieBrowser));
                group.Add("Cookies 来源", source, ButtonOf("去登录", 68, () =>
                {
                    string url = Urls(links.Text).FirstOrDefault();
                    if (String.IsNullOrEmpty(url))
                    {
                        MessageBox.Show(dialog, "请先在下载工作台粘贴视频或频道链接。");
                        return;
                    }
                    if (SignInToSite(dialog, url)) source.Choose(2);
                }));
                var cookies = FolderField(group, "Cookies 文件", composerCookies, dialog, true);
                Action updateFields = () => cookies.Parent.Parent.Enabled = source.SelectedIndex == 1;
                source.SelectionChanged += (sender, args) => updateFields();
                updateFields();
                var password = group.Text("视频密码（仅在本次运行中使用）", composerPassword);
                password.UseSystemPasswordChar = true;
                password.Enabled = (composerMode == AutoLink || composerMode == SingleLink || composerMode == Sidecars) && Urls(links.Text).Count() == 1;
                var footer = Footer();
                footer.Controls.Add(CloseButton(dialog));
                footer.Controls.Add(ButtonOf("确定", 82, () =>
                {
                    if (source.SelectedIndex == 1 && !File.Exists(cookies.Text.Trim()))
                    { MessageBox.Show(dialog, "请选择有效的 Cookies 文件。"); return; }
                    composerCookies = source.SelectedIndex == 1 ? cookies.Text.Trim() : null;
                    composerCookieBrowser = source.SelectedIndex == 0 ? null : CookieSessions.Sources[source.SelectedIndex - 1];
                    composerPassword = password.Text;
                    ResetComposerResult();
                    dialog.DialogResult = DialogResult.OK;
                }, null, null, ButtonStyle.Filled));
                dialog.Controls.Add(PreferencePage(group));
                dialog.Controls.Add(footer);
                ShowAppDialog(dialog, this);
            }
        }

        private string SafeComposerError(Exception error)
        {
            return String.IsNullOrEmpty(composerPassword) ? error.Message : error.Message.Replace(composerPassword, "[视频密码]");
        }

        private static string DurationText(string input)
        {
            double seconds;
            return Double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds > 0 && seconds < 86400000
                ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss") : "";
        }

        private static string FormatSize(Dictionary<string, object> format)
        {
            double bytes;
            return Double.TryParse(Engine.Value(format, "filesize"), NumberStyles.Float, CultureInfo.InvariantCulture, out bytes) && bytes > 0
                ? SizeText(bytes) :
                Double.TryParse(Engine.Value(format, "filesize_approx"), NumberStyles.Float, CultureInfo.InvariantCulture, out bytes) && bytes > 0
                    ? "约 " + SizeText(bytes) : "未知";
        }
    }
}
