using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TubeGet
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
                return SelfTest.Run(args.Skip(1).ToArray()).GetAwaiter().GetResult();
            if (args.Length > 0 && args[0] == "--ui-smoke")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string captureDirectory = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
                if (captureDirectory != null) Directory.CreateDirectory(captureDirectory);
                try
                {
                    foreach (string theme in new[] { "light", "dark" })
                    {
                        Material.SetTheme(Material.ResolveDark(theme));
                        using (var form = new MainForm(new string[0], new Options { AutoStart = false, Theme = theme }, smokeTest: true))
                            form.SmokeCheck(captureDirectory);
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    if (captureDirectory != null) File.WriteAllText(Path.Combine(captureDirectory, "smoke-error.log"), ex.ToString());
                    return 1;
                }
            }
            using (var mutex = new Mutex(false, WindowActivation.MutexName))
            {
                var waiting = Stopwatch.StartNew();
                while (!TryAcquireInstance(mutex))
                {
                    if (WindowActivation.Request()) return 0;
                    if (waiting.Elapsed >= TimeSpan.FromSeconds(8)) return 1;
                    Thread.Sleep(50);
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var options = Engine.Read("settings.json", new Options()) ?? new Options();
                    options.Theme = MainForm.NormalizeTheme(options.Theme);
                    Material.SetTheme(Material.ResolveDark(options.Theme));
                    using (var form = new MainForm(args, options))
                    using (var activation = new WindowActivation(form.ShowMain))
                        Application.Run(form);
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }

        private static bool TryAcquireInstance(Mutex mutex)
        {
            try { return mutex.WaitOne(0); }
            catch (AbandonedMutexException) { return true; }
        }
    }

    internal sealed partial class MainForm : FirstPaintWindow
    {
        private readonly List<MediaTask> jobs;
        private readonly string[] startupUrls;
        private readonly bool smokeTest;
        private Options options;
        private readonly Dictionary<MediaTask, CancellationTokenSource> active = new Dictionary<MediaTask, CancellationTokenSource>();
        private readonly List<MaterialButton> nav = new List<MaterialButton>();
        private readonly FlowLayoutPanel bar = new FlowLayoutPanel();
        private readonly Panel actionBar = new Panel();
        private readonly Panel completedViewSlot = new Panel();
        private readonly Panel body = new Panel();
        private readonly ListView list = new BufferedTaskList();
        private readonly EmptyView empty = new EmptyView();
        private readonly Label status = new Label();
        private readonly ToolTip tips = new ToolTip();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 500 };
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Icon windowIcon = LoadAppIcon(SystemInformation.IconSize);
        private readonly Icon trayIcon = LoadAppIcon(SystemInformation.SmallIconSize);
        private readonly Bitmap brandIcon = LoadBrandIcon();
        private int page;
        private bool pageInitialized;
        private bool layoutScaled;
        private static readonly float UiScale = ReadUiScale();

        private static float ReadUiScale()
        {
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f;
        }

        internal static int Ui(int pixels) { return (int)Math.Round(pixels * UiScale); }

        internal static Font UiFont(float points, FontStyle style = FontStyle.Regular)
        {
            return new Font("Microsoft YaHei UI", points, style);
        }

        private static void ScaleDesign(Control control)
        {
            if (UiScale != 1f) control.Scale(new SizeF(UiScale, UiScale));
        }
        private bool dirty, quitting, forceExit, confirmingExit, advanced, streamCopy;
        private string lastClipboard = "", filter = "", conversionFormat = "mp4";

        internal MainForm(string[] args, Options saved, bool smokeTest = false)
        {
            this.smokeTest = smokeTest;
            options = saved;
            startupUrls = args;
            jobs = smokeTest ? new List<MediaTask>() : Engine.Read("tasks.json", new List<MediaTask>()).Where(j => j != null).ToList();
            var taskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var job in jobs)
            {
                Guid identifier;
                if (Guid.TryParseExact(job.Id, "N", out identifier) && taskIds.Add(job.Id)) continue;
                job.Id = Guid.NewGuid().ToString("N");
                taskIds.Add(job.Id);
                dirty = true;
            }
            conversionFormat = options.ConvertFormat ?? "mp4";
            streamCopy = options.StreamCopy;
            foreach (var j in jobs.Where(x => new[] { "解析中", "下载中", "处理文件", "转换中", "等待中", "正在暂停" }.Contains(x.State)))
            {
                j.State = !j.Local && options.Resume && !IsPausing(j) ? "等待中" : "已暂停";
                j.Speed = 0;
                dirty = !smokeTest;
            }
            Text = "TubeGet";
            Font = UiFont(10);
            AutoScaleMode = AutoScaleMode.None;
            Icon = windowIcon;
            ClientSize = new Size(1190, 750);
            MinimumSize = new Size(930, 630);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Material.Surface;
            ForeColor = Material.Ink;
            Material.ThemeWindow(this, disableTransitions: true);
            HandleCreated += (s, e) => Material.HideWindowBorder(this);
            BuildWorkspace();
            ConfigureTray();
            timer.Tick += (s, e) => Tick();
            FormClosing += ClosingWindow;
            Disposed += (s, e) =>
            {
                timer.Dispose();
                var trayMenu = tray.ContextMenuStrip;
                tray.Dispose();
                if (trayMenu != null) trayMenu.Dispose();
                tips.Dispose();
                windowIcon.Dispose();
                trayIcon.Dispose();
                brandIcon.Dispose();
                DisposeCompletedCovers();
                if (rowHeight != null) rowHeight.Dispose();
            };
            Shown += (s, e) => Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemThemeChanged;
            Disposed += (s, e) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (page == DownloadPage && e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AnalyzeComposer();
                    return;
                }
                if (page == DownloadPage && e.KeyCode == Keys.Escape && analysis != null)
                {
                    e.SuppressKeyPress = true;
                    AnalyzeComposer();
                    return;
                }
                if (e.Control && e.KeyCode == Keys.V && !(ActiveControl is TextBox) && !links.Focused)
                {
                    if (page == DownloadPage) PasteComposerInput(ClipboardText());
                    else Paste("视频");
                    e.Handled = true;
                }
                if (e.KeyCode == Keys.Delete && list.Focused) DeleteJobs(Selected());
            };
            SetPage(DownloadPage);
            ScaleDesign(this);
            ScaleDesign(downloadOptionsPanel);
            layoutScaled = true;
            ConfigureActions();
            ResizeComposerColumns();
        }

        private static Icon LoadAppIcon(Size size)
        {
            using (var resource = typeof(MainForm).Assembly.GetManifestResourceStream("TubeGet.AppIcon.ico"))
                return new Icon(resource, size);
        }

        private static Bitmap LoadBrandIcon()
        {
            int size = Ui(46);
            var image = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var icon = LoadAppIcon(new Size(256, 256)))
            using (var source = icon.ToBitmap())
            using (var graphics = Graphics.FromImage(image))
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, source.Width, source.Height,
                    GraphicsUnit.Pixel, attributes);
            }
            return image;
        }

        protected override void OnFirstPaintComplete()
        {
            timer.Start();
            if (options.AutoUpdateComponents) StartComponentUpdate(options.Proxy);
            lastClipboard = ClipboardText();
            Schedule();
            if (startupUrls.Length > 0)
                AddUrls(String.Join("\n", startupUrls), "视频", options.Playlist, null, 0);
            if (options.AutoUpdateApp) CheckAppUpdate(true);
        }

        internal void SmokeCheck(string captureDirectory)
        {
            jobs.Clear();
            CreateControl();
            PerformLayout();
            for (int i = 0; i < nav.Count; i++)
            {
                SetPage(i);
                if ((i != DownloadPage && bar.Controls.Count < 2) || heroActions.Controls.Count == 0 || status.Text.Length == 0)
                    throw new InvalidOperationException("工作区初始化失败：" + i);
                if (captureDirectory != null && i == DownloadPage)
                    CaptureLayout(this, captureDirectory, "workspace");
                if (captureDirectory != null && i == 2)
                    CaptureLayout(this, captureDirectory, "workspace-convert");
                if (i == DownloadPage && captureDirectory != null)
                {
                    OpenComposer(AutoLink);
                    PasteComposerInput("https://example.test/first\nhttps://example.test/second\nhttps://example.test/first");
                    if (composerMode != AutoLink || resultMode != TextLinks || choices.Items.Count != 2 ||
                        choices.CheckedItems.Count != 2 || resultCount.Text != "2 项" || preview.Visible ||
                        selectAllCheck.CheckState != CheckState.Checked || !selectAllCheck.Visible ||
                        analyzeComposerButton.Parent != pasteComposerButton.Parent || !analyzeComposerButton.Visible)
                        throw new InvalidOperationException("批量链接去重或勾选失败。");
                    choices.Items[0].Checked = false;
                    if (selectAllCheck.CheckState != CheckState.Indeterminate)
                        throw new InvalidOperationException("批量部分勾选状态错误。");
                    selectAllCheck.AccessibilityObject.DoDefaultAction();
                    if (choices.CheckedItems.Count != 2 || !addButton.Enabled || selectAllCheck.CheckState != CheckState.Checked)
                        throw new InvalidOperationException("批量全选失败。");
                    selectAllCheck.AccessibilityObject.DoDefaultAction();
                    if (choices.CheckedItems.Count != 0 || choices.Items.Count != 2 || addButton.Enabled ||
                        selectAllCheck.CheckState != CheckState.Unchecked)
                        throw new InvalidOperationException("批量取消勾选失败。");
                    selectAllCheck.AccessibilityObject.DoDefaultAction();
                    string queuedInput = links.Text;
                    addButton.PerformClick();
                    dirty = false;
                    if (page != DownloadPage || nav.Count != 3 || list.Parent != downloadQueueHost || list.Items.Count != 2 ||
                        links.Text != queuedInput || choices.Items.Count != 2 || downloadQueueCount.Text != "2 项" ||
                        jobs.Any(job => job.Folder != destination.Text))
                        throw new InvalidOperationException("加入下载队列后，解析结果或下载队列没有同时保留。");
                    jobs[0].Title = "Blender 动画入门";
                    jobs[0].State = "下载中";
                    jobs[0].Percent = 64;
                    jobs[0].Speed = 3.2 * 1048576;
                    jobs[1].Title = "UE5 新手教程";
                    jobs[1].State = "等待中";
                    RefreshTasks();
                    CaptureLayout(this, captureDirectory, "composer-batch");
                    SetPage(1);
                    if (list.Parent != taskListHost || list.Items.Count != 0) throw new InvalidOperationException("已完成页的队列切换失败。");
                    SetPage(DownloadPage);
                    if (list.Parent != downloadQueueHost || list.Items.Count != 2 || choices.Items.Count != 2)
                        throw new InvalidOperationException("返回下载工作台后状态丢失。");
                    var originalSize = Size;
                    try
                    {
                        Size = MinimumSize;
                        PerformLayout();
                        if (pasteComposerButton.Right > pasteComposerButton.Parent.ClientSize.Width || links.Width < 110 ||
                            choices.Width < 250 || list.Parent != downloadQueueHost || downloadQueueHost.Width < 220 ||
                            addButton.Right > addButton.Parent.ClientSize.Width)
                            throw new InvalidOperationException("下载工作台的控件在最小窗口中被裁切。");
                        CaptureLayout(this, captureDirectory, "composer-compact");
                    }
                    finally { Size = originalSize; }
                    using (var pending = new CancellationTokenSource())
                    {
                        string input = links.Text;
                        analysis = pending;
                        clearListButton.PerformClick();
                        if (links.Text != input || choices.Items.Count != 0 || analyzedUrl != null ||
                            addButton.Enabled || clearListButton.Enabled || selectAllCheck.Visible || analysis != null ||
                            list.Items.Count != 2 ||
                            !SpinWait.SpinUntil(() => pending.IsCancellationRequested, 2000))
                            throw new InvalidOperationException("清空列表、保留输入或取消解析失败。");
                    }
                    AnalyzeComposer();
                    var previousRows = choices.Items.Cast<ListViewItem>().ToArray();
                    var previousQueue = jobs.ToArray();
                    choices.Items[0].Checked = false;
                    clearComposerButton.PerformClick();
                    if (links.TextLength != 0 || clearComposerButton.Enabled || !addButton.Enabled ||
                        !choices.Items.Cast<ListViewItem>().SequenceEqual(previousRows) || choices.Items[0].Checked ||
                        !jobs.SequenceEqual(previousQueue) || list.Items.Count != 2)
                        throw new InvalidOperationException("清空链接时影响了解析结果或下载队列。");
                    SetComposerInput("https://example.test/new-work");
                    if (!choices.Items.Cast<ListViewItem>().SequenceEqual(previousRows) || !jobs.SequenceEqual(previousQueue))
                        throw new InvalidOperationException("编辑链接时影响了解析结果或下载队列。");
                    jobs.Clear();
                    RefreshTasks();
                    var channel = Engine.Json().Deserialize<Dictionary<string, object>>(
                        "{\"_type\":\"playlist\",\"entries\":[{\"entries\":[{\"url\":\"https://example.test/a\"}]}," +
                        "{\"entries\":[{\"ie_key\":\"Youtube\",\"id\":\"abcdefghijk\"}]}]}");
                    if (!Engine.IsCollectionResult(channel) || CollectionLeaves(channel).Count() != 2 ||
                        CollectionUrl(CollectionLeaves(channel).Last()) != "https://www.youtube.com/watch?v=abcdefghijk")
                        throw new InvalidOperationException("嵌套频道列表识别失败。");
                    OpenComposer(AutoLink, "https://example.test/collection");
                    ResetComposerResult();
                    ConfigureComposerResult(CollectionLinks);
                    analyzedUrl = "https://example.test/collection";
                    channel["title"] = "示例合集";
                    collectionDirectoryName = Engine.CollectionDirectoryName(channel);
                    ShowMediaMetadata(channel, analyzedUrl, 2);
                    const string sampleThumbnail = "https://example.test/thumbnail.jpg";
                    collectionPreviews.Add(sampleThumbnail, new Bitmap(96, 54));
                    preview.Image = new Bitmap(160, 90);
                    AddCollectionRows(CollectionLeaves(channel).Select(item => new Entry {
                        Url = CollectionUrl(item), Title = "示例视频", Thumbnail = sampleThumbnail
                    }));
                    if (!mediaDetails.Visible || !preview.Visible || !copyLinkButton.Visible || !summary.Text.Contains("2 项视频"))
                        throw new InvalidOperationException("合集封面区未正确显示。");
                    CaptureLayout(this, captureDirectory, "composer-collection");
                    int checkboxY = choices.Items[0].Bounds.Top + choices.Items[0].Bounds.Height / 2;
                    var checkboxPoint = new IntPtr((checkboxY << 16) | (choices.CheckboxClickWidth - 2));
                    SendListMessage(choices.Handle, 0x0201, new IntPtr(1), checkboxPoint);
                    SendListMessage(choices.Handle, 0x0202, IntPtr.Zero, checkboxPoint);
                    if (choices.Items[0].Checked || selectAllCheck.CheckState != CheckState.Indeterminate)
                        throw new InvalidOperationException("点击合集复选框后未正确更新部分选中状态。");
                    SetAllComposer(false);
                    if (choices.CheckedItems.Count != 0 || selectAllCheck.CheckState != CheckState.Unchecked)
                        throw new InvalidOperationException("合集取消全选失败。");
                    SetAllComposer(true);
                    if (choices.CheckedItems.Count != 2 || selectAllCheck.CheckState != CheckState.Checked)
                        throw new InvalidOperationException("合集全选失败。");
                    choices.Items[0].Checked = false;
                    addButton.PerformClick();
                    dirty = false;
                    if (jobs.Count != 1 || jobs[0].Folder != Path.Combine(destination.Text, "示例合集") ||
                        jobs[0].PlaylistDirectoryName != "示例合集")
                        throw new InvalidOperationException("勾选合集视频未保存到同名目录。");
                    VerifyComposerClear();
                    jobs.Clear();
                    RefreshTasks();
                    OpenComposer(AutoLink, "https://example.test/video");
                    var fixture = Engine.Json().Deserialize<Dictionary<string, object>>(
                        "{\"formats\":[{\"format_id\":\"audio\",\"vcodec\":\"none\",\"acodec\":\"aac\",\"ext\":\"m4a\",\"language\":\"en\"}," +
                        "{\"format_id\":\"video\",\"vcodec\":\"h264\",\"acodec\":\"none\",\"ext\":\"mp4\",\"resolution\":\"1920x1080\",\"fps\":30,\"filesize_approx\":11639194}]," +
                        "\"subtitles\":{\"zh-Hans\":[]},\"automatic_captions\":{\"en\":[]}}");
                    if (Engine.IsCollectionResult(fixture) || resultMode != SingleLink)
                        throw new InvalidOperationException("单个链接被识别为列表。");
                    analyzedUrl = "https://example.test/video";
                    PopulateComposerFormats(fixture, false);
                    SetSubtitleChoices(fixture, false);
                    UpdateComposerSelection();
                    if (choices.Items.Count != 2 || !audioPicker.Enabled || !addButton.Enabled || subtitleIds.Length != 4)
                        throw new InvalidOperationException("画质、音轨或字幕选项未加载。");
                    fixture["title"] = "海底世界 · 画质与音轨演示";
                    fixture["uploader"] = "示例作者";
                    fixture["duration"] = 40;
                    ShowMediaMetadata(fixture, analyzedUrl);
                    if (!mediaDetails.Visible || !copyLinkButton.Visible || !reanalyzeButton.Visible ||
                        choices.Items[0].Text != "1080p · 全高清" || choices.Items[0].SubItems[1].Text != "H.264")
                        throw new InvalidOperationException("作品详情或画质标签显示错误。");
                    preview.Image = new Bitmap(160, 90);
                    preview.Visible = true;
                    // Enough rows to force native scrollbar layout while format results are refreshed.
                    var originalFormats = Engine.Rows(fixture, "formats").ToArray();
                    var extraFormats = Enumerable.Range(0, 16).Select(index =>
                        {
                            var row = new Dictionary<string, object>(originalFormats[1]);
                            row["format_id"] = "video-" + index;
                            return row;
                        }).ToArray();
                    fixture["formats"] = extraFormats.Concat(originalFormats).ToArray();
                    choices.Items.Clear();
                    PopulateComposerFormats(fixture, false);
                    if (choices.Items.Count != 18 || choices.SelectedItems.Count != 1)
                        throw new InvalidOperationException("画质列表刷新后未正确显示所有条目。");
                    int formatY = choices.Items[1].Bounds.Top + choices.Items[1].Bounds.Height / 2;
                    var formatCheckPoint = new IntPtr((formatY << 16) | (choices.CheckboxClickWidth - 2));
                    SendListMessage(choices.Handle, 0x0201, new IntPtr(1), formatCheckPoint);
                    SendListMessage(choices.Handle, 0x0202, IntPtr.Zero, formatCheckPoint);
                    if (!choices.Items[1].Selected || choices.Items[0].Selected || choices.SelectedItems.Count != 1)
                        throw new InvalidOperationException("点击画质勾选区域后未正确保持单选。");
                    choices.Items[0].Selected = true;
                    var defaultSize = Size;
                    try
                    {
                        Size = MinimumSize;
                        PerformLayout();
                        if (preview.Width < 80 || mediaTitle.Width < 120 || choices.Height < 70 ||
                            reanalyzeButton.Bounds.IntersectsWith(addButton.Bounds))
                            throw new InvalidOperationException("作品详情在最小窗口中被裁切。");
                        string sizeLabel = choices.Items[0].SubItems[2].Text;
                        int required = TextRenderer.MeasureText(sizeLabel, Font, Size.Empty,
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                        if (sizeLabel != "约 11.1 MB" || required > choices.Columns[2].Width - Ui(8))
                            throw new InvalidOperationException("最小窗口无法完整显示文件大小。");
                        CaptureLayout(this, captureDirectory, "composer-formats-compact");
                    }
                    finally { Size = defaultSize; }
                    CaptureLayout(this, captureDirectory, "composer-formats");
                    bool optionsOpened = false;
                    using (var closeOptions = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        closeOptions.Tick += (sender, args) =>
                        {
                            closeOptions.Stop();
                            var dialog = downloadOptionsPanel.FindForm();
                            optionsOpened = dialog != null && dialog.Text == "下载选项" && audioPicker.Visible;
                            if (dialog != null) dialog.DialogResult = DialogResult.OK;
                        };
                        closeOptions.Start();
                        ShowComposerOptions();
                    }
                    if (!optionsOpened || downloadOptionsPanel.IsDisposed || downloadOptionsPanel.Parent != null)
                        throw new InvalidOperationException("下载选项无法打开或无法复用。");
                    string parsedSource = analyzedUrl;
                    var selectedFormat = choices.SelectedItems[0];
                    clearComposerButton.PerformClick();
                    if (links.TextLength != 0 || analyzedUrl != parsedSource || !mediaDetails.Visible ||
                        choices.SelectedItems.Count != 1 || choices.SelectedItems[0] != selectedFormat || !addButton.Enabled)
                        throw new InvalidOperationException("清空链接时影响了单视频画质和选中项。");
                    VerifyComposerClear();
                    CaptureLayout(this, captureDirectory, "composer-cleared");
                }
            }
            if (captureDirectory != null)
            {
                jobs.Add(new MediaTask { Title = "Blender 动画入门", Source = "https://example.test/a", Kind = "视频", State = "已完成" });
                jobs.Add(new MediaTask { Title = "城市漫步", Source = "https://example.test/b", Kind = "视频", State = "已完成" });
                jobs.Add(new MediaTask { Title = "示例音轨", Source = "https://example.test/c", Kind = "音频", State = "已完成" });
                SetPage(1);
                list.Items[0].Selected = true;
                CaptureLayout(this, captureDirectory, "completed-list");
                completedViewButton.PerformClick();
                if (!options.CompletedCards || completedViewButton.Glyph != "queue" ||
                    list.View != View.LargeIcon || list.Items.Count != 3 || Selected().Length != 1)
                    throw new InvalidOperationException("切换封面卡片时，任务或选择状态丢失。");
                CaptureLayout(this, captureDirectory, "completed-cards");
                filter = "城市";
                RefreshTasks();
                if (list.Items.Count != 1 || list.Items[0].Text != "城市漫步")
                    throw new InvalidOperationException("封面卡片搜索失败。");
                completedViewButton.PerformClick();
                if (options.CompletedCards || completedViewButton.Glyph != "grid" ||
                    list.View != View.Details || list.Items.Count != 1)
                    throw new InvalidOperationException("切换回列表时，搜索结果丢失。");
                jobs.Clear();
                RefreshTasks();
                VerifyCompletedFolderNavigation(captureDirectory);
            }
            using (var dialog = CreatePreferencesDialog(0))
            {
                ScaleDesign(dialog);
                dialog.CreateControl();
                dialog.PerformLayout();
                if (captureDirectory != null) CaptureLayout(dialog, captureDirectory, "settings");
            }
            using (var dialog = CreatePreferencesDialog(4))
            {
                ScaleDesign(dialog);
                dialog.CreateControl();
                dialog.PerformLayout();
                if (captureDirectory != null) CaptureLayout(dialog, captureDirectory, "settings-components");
            }
            using (var dialog = CreatePreferencesDialog(5))
            {
                ScaleDesign(dialog);
                dialog.CreateControl();
                dialog.PerformLayout();
                if (captureDirectory != null) CaptureLayout(dialog, captureDirectory, "settings-ai-repair");
            }
            var aiSettings = options.AiRepair;
            try
            {
                options.AiRepair = new AiRepairSettings { Provider = "codex" };
                using (var dialog = CreatePreferencesDialog(5))
                {
                    ScaleDesign(dialog);
                    dialog.CreateControl();
                    dialog.PerformLayout();
                    if (captureDirectory != null) CaptureLayout(dialog, captureDirectory, "settings-ai-local");
                }
            }
            finally { options.AiRepair = aiSettings; }
        }

        private void VerifyCompletedFolderNavigation(string captureDirectory)
        {
            string legacy = Path.Combine(options.Downloads, "Houdini 分集");
            const string longCollection = "独立合集 · 用于验证长标题换行时封面卡片保持相同尺寸";
            string modern = Path.Combine(options.Downloads, longCollection);
            string playlist = Path.Combine(options.Downloads, "批量下载合集");
            for (int part = 1; part <= 2; part++)
                jobs.Add(new MediaTask { Title = "Houdini 分集 p0" + part, Folder = legacy,
                    Source = "https://www.bilibili.com/video/BV1Ri421v7KW?p=" + part, Kind = "视频", State = "已完成", Percent = 100 });
            jobs.Add(new MediaTask { Title = "编织演示", Folder = modern, PlaylistDirectoryName = longCollection,
                Source = "https://example.test/new", Kind = "视频", State = "已完成", Percent = 100 });
            jobs.Add(new MediaTask { Title = "批量下载合集", Folder = options.Downloads, PlaylistDirectoryName = "批量下载合集",
                Files = new[] { Path.Combine(playlist, "01.mp4"), Path.Combine(playlist, "02.mp4") },
                Source = "https://example.test/playlist", Playlist = true, Kind = "视频", State = "已完成", Percent = 100 });
            jobs.Add(new MediaTask { Title = "单个视频", Folder = options.Downloads,
                Source = "https://example.test/single", Kind = "视频", State = "已完成", Percent = 100 });
            filter = "";
            RefreshTasks();
            if (list.Items.Count != 4 || list.Items.Cast<ListViewItem>().Count(item => item.Tag is CompletedFolder) != 3)
                throw new InvalidOperationException("已完成的合集未按文件夹分组。");
            VerifyCompletedViewButtonPosition();
            CaptureLayout(this, captureDirectory, "completed-folders");

            completedViewButton.PerformClick();
            CaptureLayout(this, captureDirectory, "completed-collection-grid");
            filter = "Houdini";
            RefreshTasks();
            var legacyFolder = list.Items.Cast<ListViewItem>().Select(item => item.Tag).OfType<CompletedFolder>().Single();
            OpenTaskRow(legacyFolder);
            if (list.View != View.Details || list.Items.Count != 2 || heading.Text != "Houdini 分集" ||
                list.Items.Cast<ListViewItem>().Any(item => !(item.Tag is MediaTask)))
                throw new InvalidOperationException("进入合集后未正确显示分集列表。");
            VerifyCompletedViewButtonPosition();
            CaptureLayout(this, captureDirectory, "completed-folder-content");
            list.Items[0].Selected = true;
            completedViewButton.PerformClick();
            if (list.View != View.LargeIcon || !options.CompletedFolderCards || list.SelectedItems.Count != 1 ||
                list.Items.Count != 2 || completedViewButton.Glyph != "queue")
                throw new InvalidOperationException("合集内切换到封面卡片时丢失分集或选中项。");
            CaptureLayout(this, captureDirectory, "completed-folder-cards");
            completedViewButton.PerformClick();
            if (list.View != View.Details || options.CompletedFolderCards || !options.CompletedCards ||
                completedViewButton.Glyph != "grid")
                throw new InvalidOperationException("合集内切换回列表时影响了首页视图。");
            bar.Controls.Cast<Control>().OfType<MaterialButton>().Single(button => button.AccessibleName == "返回已完成首页").PerformClick();
            if (list.View != View.LargeIcon || completedFolder != null || filter != "Houdini" || list.Items.Count != 1)
                throw new InvalidOperationException("返回已完成页后未恢复原视图和搜索结果。");

            filter = "";
            RefreshTasks();
            OpenTaskRow(list.Items.Cast<ListViewItem>().Select(item => item.Tag).OfType<CompletedFolder>()
                .Single(folder => folder.Name == "批量下载合集"));
            if (list.Items.Count != 2 || list.Items.Cast<ListViewItem>().Any(item => !(item.Tag is CompletedFile)))
                throw new InvalidOperationException("整合集下载未显示各个输出文件。");
            completedViewButton.PerformClick();
            if (list.View != View.LargeIcon || list.Items.Count != 2 || !options.CompletedFolderCards)
                throw new InvalidOperationException("整合集下载未能切换为文件卡片。");
            NavigateCompletedFolder(null);
            completedViewButton.PerformClick();
            options.CompletedFolderCards = false;
            jobs.Clear();
            RefreshTasks();
        }

        private void VerifyCompletedViewButtonPosition()
        {
            int right = actionBar.PointToClient(completedViewButton.PointToScreen(new Point(completedViewButton.Width, 0))).X;
            if (!completedViewButton.Visible || right != actionBar.ClientSize.Width)
                throw new InvalidOperationException("视图切换按钮没有对齐工具栏右侧。");
        }

        private void VerifyComposerClear()
        {
            string input = links.Text;
            int queued = list.Items.Count;
            Func<bool> isCleared = () => emptyResults.Visible && !mediaDetails.Visible && !choices.Visible &&
                !composerTableHeader.Visible && !addButton.Enabled && !clearListButton.Enabled &&
                preview.Image == null && choices.Items.Count == 0 && analyzedUrl == null &&
                collectionPreviews.Count == 0 && links.Text == input && list.Items.Count == queued &&
                choiceStatus.Text.StartsWith("已清空列表", StringComparison.Ordinal);
            bool incompleteFrame = false;
            PaintEventHandler observe = (sender, args) => incompleteFrame |= !isCleared();
            composerResultsCard.Paint += observe;
            try { clearListButton.PerformClick(); }
            finally { composerResultsCard.Paint -= observe; }
            if (incompleteFrame || !isCleared())
                throw new InvalidOperationException("清空解析结果时出现了中间画面，或影响了输入和下载队列。");
        }

        private static void CaptureLayout(Form form, string directory, string name)
        {
            if (!form.Visible)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(SystemInformation.VirtualScreen.Right + 24, SystemInformation.VirtualScreen.Top);
                form.Show();
                form.Refresh();
            }
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, name + (Material.Dark ? "-dark.png" : "-light.png")),
                    System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        internal void ShowMain()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            UpdateTray();
        }

        private Button ButtonOf(string text, int width, Action click, string tip = null,
            string glyph = null, ButtonStyle appearance = ButtonStyle.Tonal)
        {
            var button = new MaterialButton {
                Text = text, AccessibleName = tip ?? text, Width = width, Height = 36,
                Margin = new Padding(0, 0, 7, 0), Glyph = glyph, Appearance = appearance
            };
            if (appearance == ButtonStyle.Navigation)
            {
                var navigationFont = UiFont(Font.SizeInPoints, FontStyle.Bold);
                button.Font = navigationFont;
                button.Disposed += (s, e) => navigationFont.Dispose();
            }
            if (click != null) button.Click += (s, e) => click();
            if (tip != null) tips.SetToolTip(button, tip);
            return button;
        }

        private static ToolStripMenuItem Item(string label, Action action)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (s, e) => action();
            return item;
        }

        private static ContextMenuStrip BuildMenu(params ToolStripItem[] items)
        {
            var menu = new ContextMenuStrip {
                BackColor = Material.Card, ForeColor = Material.Ink, Font = UiFont(10),
                Renderer = new ToolStripProfessionalRenderer(new MaterialMenuColors())
            };
            menu.Items.AddRange(items);
            return menu;
        }

        private void Attach(Button button, Func<ContextMenuStrip> build, bool alignRight = false)
        {
            button.Click += (s, e) => ShowMenu(build(), button,
                new Point(alignRight ? button.Width : 0, button.Height + (alignRight ? Ui(4) : 0)),
                alignRight ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.Default);
        }

        private void ShowMenu(ContextMenuStrip menu, Control parent, Point location,
            ToolStripDropDownDirection direction = ToolStripDropDownDirection.Default)
        {
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            if (direction == ToolStripDropDownDirection.Default) menu.Show(parent, location);
            else menu.Show(parent, location, direction);
        }

        private ContextMenuStrip MainMenu()
        {
            var pinned = TrayItem("窗口置顶", "window", () => TopMost = !TopMost);
            pinned.Checked = TopMost;
            var font = UiFont(10);
            var menu = new ContextMenuStrip {
                BackColor = Material.Card, ForeColor = Material.Ink, Font = font,
                ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(Ui(6)),
                MinimumSize = new Size(Ui(224), 0), Renderer = new MaterialPopupRenderer(rounded: true),
                DropShadowEnabled = false
            };
            menu.Disposed += (s, e) => font.Dispose();
            menu.SizeChanged += (s, e) =>
            {
                if (menu.Width < 2 || menu.Height < 2) return;
                var previous = menu.Region;
                using (var outline = Material.Shape(menu.ClientRectangle, Ui(12))) menu.Region = new Region(outline);
                if (previous != null) previous.Dispose();
            };
            menu.Items.AddRange(new ToolStripItem[] {
                TrayItem("参数设置", "settings", () => Settings(0)), TrayItem("设置 Cookies", "clipboard", () => Settings(2)), pinned,
                TrayItem("最小化到托盘", "download", HideToTray), new ToolStripSeparator(),
                TrayItem("数据文件夹", "folder", () => OpenFolder(Engine.Data)), TrayItem("导出任务", "back", ExportTasks),
                TrayItem("导入任务", "plus", ImportTasks), new ToolStripSeparator(),
                TrayItem("支持的站点", "queue", SupportedSites), TrayItem("检查程序更新", "convert", () => CheckAppUpdate(false)),
                TrayItem("关于 TubeGet", "done", ShowAbout)
            });
            return menu;
        }

        private void ShowAbout()
        {
            using (var dialog = Dialog("关于 TubeGet", 460, 300, new FirstPaintWindow()))
            {
                string info = "TubeGet " + ReleaseInfo.Version + Environment.NewLine +
                    "Windows " + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + Environment.NewLine + Environment.NewLine +
                    String.Join(Environment.NewLine, ComponentUpdater.Components.Select(component =>
                        component.Name + " " + Engine.ComponentUpdates.InstalledVersion(component)));
                var details = new TextBox {
                    Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                    BackColor = Material.Card, ForeColor = Material.Ink, Text = info, AccessibleName = "TubeGet 版本与组件"
                };
                var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Material.Card };
                host.Controls.Add(details);
                var footer = Footer();
                footer.Controls.Add(CloseButton(dialog));
                footer.Controls.Add(ButtonOf("复制版本信息", 112, () => Copy(info)));
                dialog.Controls.Add(host);
                dialog.Controls.Add(footer);
                ShowAppDialog(dialog, this);
            }
        }

        private void SetPage(int index, bool reloadActions = false)
        {
            if (pageInitialized && page == index && !reloadActions)
            {
                if (index == 1 && completedFolder != null) NavigateCompletedFolder(null);
                else RefreshTasks();
                return;
            }
            if (!pageInitialized || page != index)
            {
                completedFolder = null;
                completedRootFilter = filter = "";
            }
            pageInitialized = true;
            page = index;
            PagePainting.Update(taskCard, () =>
            {
                heading.Text = index == 1 && completedFolder != null ? Path.GetFileName(completedFolder) : PageNames[index];
                composer.Visible = index == DownloadPage;
                taskListHost.Visible = index != DownloadPage;
                ConfigureActions();
                AttachTaskList();
                RefreshTasks();
            });
            for (int i = 0; i < nav.Count; i++)
            {
                nav[i].Current = i == index;
                nav[i].Invalidate();
            }
        }

        private IEnumerable<MediaTask> EditableConversions()
        {
            return jobs.Where(j => j.Kind == "转换" && j.State != "已完成" && !active.ContainsKey(j));
        }

        private void AddQueueButtons()
        {
            bar.Controls.Add(ButtonOf("全部开始", 106, () => ResumeJobs(VisibleJobs().ToArray()), null, "play"));
            bar.Controls.Add(ButtonOf("全部暂停", 106, () => PauseJobs(VisibleJobs().ToArray()), null, "pause"));
            bar.Controls.Add(ButtonOf("", 40, () => DeleteJobs(Selected()), "删除选中任务", "delete"));
        }

        private ContextMenuStrip DownloadMenu()
        {
            var toggle = Item("高级模式", () => advanced = !advanced); toggle.Checked = advanced;
            var watch = Item("监测剪贴板", () =>
            {
                lastClipboard = ClipboardText();
                options.Watch = !options.Watch;
                SaveOptions();
            });
            watch.Checked = options.Watch;
            return BuildMenu(Item("下载视频", () => Paste("视频")), Item("下载音频", () => Paste("音频")),
                Item("字幕与封面", () => OpenComposer(Sidecars, Urls(ClipboardText()).FirstOrDefault())), Item("下载封面", () => Paste("封面")),
                Item("批量导入链接", () => OpenComposer(TextLinks)), Item("解析播放列表", () => OpenComposer(CollectionLinks, Urls(ClipboardText()).FirstOrDefault())),
                Item("视频格式选择", () => OpenComposer(SingleLink, Urls(ClipboardText()).FirstOrDefault())), Item("YouTube 搜索", () => InspectDialog(true, true)), toggle, watch);
        }

        private IEnumerable<MediaTask> VisibleJobs()
        {
            if (page == 1)
            {
                MediaTask[] visible;
                CompletedRows(out visible);
                return visible;
            }
            return jobs.Where(j => page == 2 ? j.Local : !j.Local && j.State != "已完成")
                .Where(j => MatchesTaskFilter(j.Title ?? j.Source));
        }

        private MediaTask[] Selected() { return list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<MediaTask>().ToArray(); }

        private void RefreshTasks()
        {
            MediaTask[] current;
            var rows = page == 1 ? CompletedRows(out current) : (current = VisibleJobs().ToArray()).Cast<object>().ToArray();
            bool replaceRows = !rows.Select(TaskRowKey).SequenceEqual(list.Items.Cast<ListViewItem>().Select(i => TaskRowKey(i.Tag)));
            if (replaceRows) list.BeginUpdate();
            try
            {
                if (replaceRows)
                {
                    var selected = new HashSet<object>(list.SelectedItems.Cast<ListViewItem>().Select(item => TaskRowKey(item.Tag)));
                    list.Items.Clear();
                    foreach (object entry in rows)
                    {
                        var row = new ListViewItem(new[] { "", "", "", "", "" }, 0) { Tag = entry };
                        list.Items.Add(row);
                        row.Selected = selected.Contains(TaskRowKey(entry));
                    }
                }
                for (int i = 0; i < rows.Length; i++) UpdateTaskRow(list.Items[i], rows[i]);
                UpdateDashboard(current, rows.Length);
            }
            finally
            {
                if (replaceRows) list.EndUpdate();
            }
        }

        private void UpdateTaskRow(ListViewItem row, object entry)
        {
            row.Tag = entry;
            var folder = entry as CompletedFolder;
            if (folder != null)
            {
                string[] groupCells = { folder.Name, "合集", CompletedFolderCount(folder), "已完成",
                    folder.Jobs.Max(jobItem => jobItem.Added).ToString("MM-dd HH:mm") };
                for (int c = 0; c < groupCells.Length; c++)
                    if (row.SubItems[c].Text != groupCells[c]) row.SubItems[c].Text = groupCells[c];
                row.ToolTipText = folder.Path;
                return;
            }
            var file = entry as CompletedFile;
            var job = file == null ? (MediaTask)entry : file.Job;
            string detail = String.IsNullOrWhiteSpace(job.Detail) ? "" : "  ·  " + job.Detail.Split('\n')[0].Trim();
            string[] cells = { file == null ? job.Title ?? job.Source : Path.GetFileName(file.Path), job.Local ? job.Format : job.Kind, job.Percent.ToString("0.#") + "%",
                OutputMissing(job, file == null ? job.File : file.Path) ? "文件缺失" : job.State + detail, job.Added.ToString("MM-dd HH:mm") };
            for (int c = 0; c < cells.Length; c++)
                if (row.SubItems[c].Text != cells[c]) row.SubItems[c].Text = cells[c];
            string tooltip = file == null ? job.Detail ?? job.File ?? job.Source : file.Path;
            if (row.ToolTipText != tooltip) row.ToolTipText = tooltip;
            if (page == DownloadPage && job.Speed > 0 && list.IsHandleCreated) list.Invalidate(row.Bounds);
        }

        private static bool OutputMissing(MediaTask job, string output)
        {
            return job.State == "已完成" && !String.IsNullOrWhiteSpace(output) && !File.Exists(output);
        }

        private static string SizeText(double size)
        {
            if (size >= 1099511627776d) return (size / 1099511627776d).ToString("0.0") + " TB";
            if (size >= 1073741824) return (size / 1073741824).ToString("0.0") + " GB";
            if (size >= 1048576) return (size / 1048576).ToString("0.0") + " MB";
            if (size >= 1024) return (size / 1024).ToString("0.0") + " KB";
            return size.ToString("0") + " B";
        }
    }
}
