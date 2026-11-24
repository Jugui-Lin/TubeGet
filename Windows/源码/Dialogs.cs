using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private Form Dialog(string title, int width, int height, Form dialog = null)
        {
            dialog = dialog ?? new Form();
            dialog.Text = title;
            dialog.Icon = windowIcon;
            dialog.Font = Font;
            dialog.BackColor = Material.Surface;
            dialog.ForeColor = Material.Ink;
            dialog.ClientSize = new Size(width, height);
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.ShowInTaskbar = false;
            dialog.MinimizeBox = false;
            dialog.MaximizeBox = false;
            dialog.MinimumSize = new Size(width, height);
            Material.ThemeWindow(dialog);
            dialog.AutoScaleMode = AutoScaleMode.None;
            return dialog;
        }

        private static DialogResult ShowAppDialog(Form dialog, Form owner)
        {
            // Size the layout before WinForms calculates the centered startup position.
            ScaleDesign(dialog);
            return dialog.ShowDialog(owner);
        }

        private static ComboBox Choice(IEnumerable<string> items, string value)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Material.Field(box);
            box.Items.AddRange(items.ToArray());
            box.SelectedItem = value;
            if (box.SelectedIndex < 0) box.SelectedIndex = 0;
            return box;
        }

        private static FlowLayoutPanel Footer()
        {
            return new FlowLayoutPanel {
                Dock = DockStyle.Bottom, Height = 58, BackColor = Material.Rail, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 10, 7, 6), WrapContents = false
            };
        }

        private Button CloseButton(Form dialog)
        {
            var close = ButtonOf("取消", 78, () => dialog.Close(), null, null, ButtonStyle.Quiet);
            close.DialogResult = DialogResult.Cancel;
            dialog.CancelButton = close;
            return close;
        }

        private void UrlDialog(string input, string kind, bool batch)
        {
            using (var dlg = Dialog(batch ? "批量下载" : "添加下载任务", 665, 410))
            {
                var tip = new Label { Text = "输入视频网址，每行一条。", Dock = DockStyle.Top, Height = 34, Padding = new Padding(9, 7, 0, 0) };
                var urls = new TextBox {
                    Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    Text = String.Join(Environment.NewLine, Urls(input))
                };
                Material.Field(urls);
                var form = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(8) };
                var type = Choice(new[] { "视频", "音频", "封面", "字幕" }, kind);
                var playlist = new CheckBox { Text = "下载整个播放列表", Checked = options.Playlist, AutoSize = true, Margin = new Padding(10, 5, 0, 0) };
                var numbered = new CheckBox { Text = "文件名编号", AutoSize = true, Margin = new Padding(12, 5, 0, 0) };
                var start = new NumericUpDown { Minimum = 1, Maximum = 99999, Value = 1, Width = 65 };
                var settings = new LinkLabel { Text = "字幕语言与格式", AutoSize = true, Margin = new Padding(10, 5, 0, 0) };
                settings.LinkColor = Material.Primary;
                settings.LinkClicked += (s, e) => Settings(0, dlg);
                form.Controls.Add(type); form.Controls.Add(playlist);
                form.Controls.Add(numbered); form.Controls.Add(start); form.Controls.Add(settings);
                var footer = Footer();
                footer.Controls.Add(CloseButton(dlg));
                var add = ButtonOf("下载", 88, () =>
                {
                    if (!Urls(urls.Text).Any()) { MessageBox.Show(dlg, "请输入有效的 HTTP 或 HTTPS 链接。"); return; }
                    AddUrls(urls.Text, type.Text, playlist.Checked, null, numbered.Checked ? (int)start.Value : 0);
                    dlg.DialogResult = DialogResult.OK;
                }, null, null, ButtonStyle.Filled);
                footer.Controls.Add(add);
                dlg.Controls.Add(urls); dlg.Controls.Add(tip); dlg.Controls.Add(form); dlg.Controls.Add(footer);
                ShowAppDialog(dlg, this);
            }
        }

        private sealed class SettingsPage
        {
            private readonly TableLayoutPanel table;
            internal readonly TabPage Tab;
            internal SettingsPage(string name)
            {
                Tab = new TabPage(name) { BackColor = Material.Surface, ForeColor = Material.Ink, Padding = new Padding(17), AutoScroll = true };
                table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
                Tab.Controls.Add(table);
            }
            internal void Add(string name, Control control, Button browse = null)
            {
                int row = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
                var label = new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                if (control is TextBox || control is ComboBox || control is NumericUpDown) Material.Field(control);
                label.ForeColor = Material.Muted;
                control.Dock = DockStyle.Fill;
                control.Margin = new Padding(2, 5, 4, 0);
                table.Controls.Add(label, 0, row);
                table.Controls.Add(control, 1, row);
                if (browse != null) table.Controls.Add(browse, 2, row);
                else table.SetColumnSpan(control, 2);
            }
            internal TextBox Text(string name, string value)
            {
                var input = new TextBox { Text = value ?? "" };
                Add(name, input);
                return input;
            }
            internal CheckBox Check(string name, bool value)
            {
                var box = new CheckBox { Text = name, Checked = value };
                Add("", box);
                return box;
            }
            internal ComboBox Select(string name, string[] items, string value)
            {
                var input = Choice(items, value);
                Add(name, input);
                return input;
            }
        }

        private TextBox FolderField(PreferenceCard p, string name, string value, Form owner, bool file = false)
        {
            var box = new TextBox { Text = value ?? "" };
            var browse = ButtonOf("浏览", 68, () =>
            {
                if (file)
                {
                    using (var dlg = new OpenFileDialog { Filter = "Netscape Cookie 文件|*.txt|所有文件|*.*" })
                        if (dlg.ShowDialog(owner) == DialogResult.OK) box.Text = dlg.FileName;
                }
                else
                {
                    using (var dlg = new FolderBrowserDialog { SelectedPath = box.Text, Description = name })
                        if (dlg.ShowDialog(owner) == DialogResult.OK) box.Text = dlg.SelectedPath;
                }
            });
            p.Add(name, box, browse);
            return box;
        }

        private void Settings(int selected, Form owner = null)
        {
            using (var dlg = CreatePreferencesDialog(selected)) ShowAppDialog(dlg, owner ?? this);
        }

        private Form CreatePreferencesDialog(int selected)
        {
            var dlg = Dialog("参数设置", 830, 497, new FirstPaintWindow());
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            var download = new PreferenceCard("下载输出");
            var extras = new PreferenceCard("字幕与附加内容");
            var convert = new PreferenceCard("转换输出");
            var network = new PreferenceCard("网络与连接");
            var general = new PreferenceCard("外观与行为");
            var dest = FolderField(download, "下载目录", options.Downloads, dlg);
            var format = download.Select("视频容器", new[] { "默认", "mp4", "mkv", "webm", "mov" }, options.Container);
            var quality = download.Select("清晰度上限", new[] { "最佳", "2160", "1440", "1080", "720", "480", "360" }, options.Quality);
            var audio = download.Select("音频格式", new[] { "mp3", "m4a", "aac", "flac", "wav", "opus", "vorbis", "best" }, options.AudioFormat);
            var parallel = download.Select("并行任务数", Enumerable.Range(1, 8).Select(n => n.ToString()).ToArray(),
                Math.Min(8, Math.Max(1, options.Parallel)).ToString());
            var langs = extras.Text("字幕语言", options.Languages);
            var subFormat = extras.Select("字幕格式", new[] { "srt", "vtt", "ass", "原始" }, options.SubtitleFormat);
            var list = extras.Check("下载整个播放列表", options.Playlist);
            var cover = extras.Check("同时保存封面", options.Cover);
            var subs = extras.Check("同时保存字幕", options.Subtitles);
            var caps = extras.Check("包括自动生成字幕", options.AutoCaptions);
            var embed = extras.Check("将字幕嵌入视频", options.EmbedSubs);
            var metadata = extras.Check("嵌入媒体元数据", options.Metadata);

            var converted = FolderField(convert, "转换目录", options.Converted, dlg);
            var defaultConvert = convert.Select("默认输出格式", ConversionFormats, conversionFormat);
            var copyStreams = convert.Check("默认无损转换（复制码流）", streamCopy);
            var proxy = network.Text("代理地址", options.Proxy);
            proxy.AccessibleDescription = "例如 http://127.0.0.1:7890 或 socks5://127.0.0.1:7890，留空使用环境默认值。";
            var rate = network.Text("限速（如 800K）", options.Rate);
            var cookieSource = network.Select("Cookies 来源", CookieSessions.Labels, CookieSessions.Label(options.CookieBrowser));
            var cookies = FolderField(network, "Cookies 文件（可留空）", options.Cookies, dlg, true);
            Action updateCookies = () => cookies.Parent.Parent.Enabled = cookieSource.SelectedIndex == 0;
            cookieSource.SelectionChanged += (sender, args) => updateCookies();
            updateCookies();
            var agent = network.Text("User-Agent", options.Agent);
            var referer = network.Text("Referer", options.Referer);
            var aria = network.Check("使用 aria2c 加速下载", options.Aria);
            var theme = general.Select("外观模式", ThemeLabels, ThemeLabels[Math.Max(0, Array.IndexOf(ThemeModes, options.Theme))]);
            var components = new TextBox { Text = Engine.ComponentDirectory, ReadOnly = true };
            general.Add("内置组件目录", components, ButtonOf("打开", 68, () => OpenFolder(Engine.ComponentDirectory)));
            var watch = general.Check("监测系统剪贴板", options.Watch);
            var auto = general.Check("新下载任务自动开始", options.AutoStart);
            var resume = general.Check("启动后继续未完成下载", options.Resume);
            var trayMode = general.Check("关闭窗口时最小化到托盘", options.Tray);
            var notify = general.Check("任务完成时显示通知", options.Notify);
            var autoUpdateApp = general.Check("启动时自动检查并下载程序更新", options.AutoUpdateApp);
            var updates = new PreferenceCard("GitHub 组件更新");
            var autoUpdate = ConfigureComponentUpdates(updates, () => proxy.Text.Trim());
            var ai = new PreferenceCard("AI 链接修复");
            var readAiRepair = ConfigureAiRepair(ai, () => proxy.Text.Trim());
            var tabs = PreferencesNavigation(selected,
                PreferencePage(download, extras), PreferencePage(convert), PreferencePage(network), PreferencePage(general),
                PreferencePage(updates), PreferencePage(ai));
            var footer = Footer();
            footer.Controls.Add(CloseButton(dlg));
            footer.Controls.Add(ButtonOf("确定", 82, () =>
            {
                try
                {
                    foreach (string path in new[] { dest.Text, converted.Text })
                        if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new ArgumentException("请填写完整的文件夹路径。");
                    if (!String.IsNullOrWhiteSpace(proxy.Text))
                    {
                        Uri uri;
                        if (!Uri.TryCreate(proxy.Text.Trim(), UriKind.Absolute, out uri) || uri.Host.Length == 0 ||
                            !new[] { "http", "https", "socks4", "socks4a", "socks5", "socks5h" }.Contains(uri.Scheme))
                            throw new ArgumentException("请输入完整的 HTTP 或 SOCKS 代理地址。");
                    }
                    if (cookieSource.SelectedIndex == 0 && cookies.Text.Trim().Length != 0 && !File.Exists(cookies.Text.Trim())) throw new FileNotFoundException("找不到指定的 Cookies 文件。");
                    var next = new Options {
                        Downloads = Path.GetFullPath(dest.Text), Converted = Path.GetFullPath(converted.Text),
                        Container = format.Text, Quality = quality.Text,
                        AudioFormat = audio.Text, Parallel = Int32.Parse(parallel.Text, CultureInfo.InvariantCulture), Languages = langs.Text.Trim(),
                        SubtitleFormat = subFormat.Text, Playlist = list.Checked, Cover = cover.Checked,
                        Subtitles = subs.Checked, AutoCaptions = caps.Checked, EmbedSubs = embed.Checked, Metadata = metadata.Checked,
                        Proxy = proxy.Text.Trim(), Rate = rate.Text.Trim(), Cookies = cookies.Text.Trim(), CookieBrowser = CookieSessions.Sources[cookieSource.SelectedIndex],
                        Agent = agent.Text, Referer = referer.Text,
                        Aria = aria.Checked, Watch = watch.Checked, AutoStart = auto.Checked, Resume = resume.Checked,
                        Tray = trayMode.Checked, Notify = notify.Checked,
                        ConvertFormat = defaultConvert.Text, StreamCopy = copyStreams.Checked,
                        Theme = ThemeModes[Array.IndexOf(ThemeLabels, theme.Text)], Transcode = options.Transcode,
                        CompletedCards = options.CompletedCards, CompletedFolderCards = options.CompletedFolderCards,
                        AutoUpdateComponents = autoUpdate.Checked,
                        AutoUpdateApp = autoUpdateApp.Checked,
                        AiRepair = readAiRepair()
                    };
                    Engine.Save("settings.json", next);
                    options = next;
                    ApplyTheme();
                    conversionFormat = defaultConvert.Text; streamCopy = copyStreams.Checked;
                    lastClipboard = ClipboardText(); UpdateTray();
                    dlg.DialogResult = DialogResult.OK;
                    SetPage(page, true); Schedule();
                }
                catch (Exception ex) { MessageBox.Show(dlg, ex.Message, "设置无效"); }
            }, null, null, ButtonStyle.Filled));
            dlg.Controls.Add(tabs); dlg.Controls.Add(footer);
            return dlg;
        }

        private void InspectDialog(bool playlist, bool search, string initial = null)
        {
            using (var dlg = Dialog(search ? "YouTube 搜索" : playlist ? "解析播放列表" : "视频格式选择", 765, 500))
            
            {
                var input = new TextBox { Dock = DockStyle.Fill, Text = initial ?? (search ? "" : Urls(ClipboardText()).FirstOrDefault()) ?? "" };
                Material.Field(input);
                var top = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8) };
                var go = ButtonOf(search ? "搜索" : "解析", 83, null);
                go.Dock = DockStyle.Right;
                top.Controls.Add(input); top.Controls.Add(go);
                var result = new ListView {
                    Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                    CheckBoxes = playlist, MultiSelect = playlist, HideSelection = false,
                    BackColor = Material.Card, ForeColor = Material.Ink, BorderStyle = BorderStyle.None
                };
                result.Columns.Add(playlist ? "标题" : "格式 ID", playlist ? 460 : 105);
                result.Columns.Add(playlist ? "时长（秒）" : "容器", 95);
                result.Columns.Add(playlist ? "来源" : "分辨率 / 帧率", 147);
                if (!playlist) { result.Columns.Add("编码", 260); result.Columns.Add("大小", 105); }
                var note = new Label { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 5, 0, 0), Text = search ? "输入搜索关键词，显示前 25 条结果。" : "输入链接后点击解析。" };
                var footer = Footer(); footer.Controls.Add(CloseButton(dlg));
                string analyzed = null, collectionFolder = null, collectionCover = null;
                var add = ButtonOf("下载选中项", 117, () =>
                {
                    if (analyzed == null) return;
                    if (playlist)
                    {
                        var rows = result.CheckedItems.Cast<ListViewItem>().Concat(result.SelectedItems.Cast<ListViewItem>()).Distinct().ToArray();
                        if (rows.Length == 0) return;
                        AddUrls(String.Join("\n", rows.Select(r => (string)r.Tag)), "视频", false, null, 0, folder: collectionFolder,
                            playlistDirectoryName: collectionFolder == null ? null : Path.GetFileName(collectionFolder),
                            collectionCoverUrl: collectionCover);
                    }
                    else
                    {
                        if (result.SelectedItems.Count == 0) return;
                        AddUrls(analyzed, "视频", false, (string)result.SelectedItems[0].Tag, 0);
                    }
                    dlg.DialogResult = DialogResult.OK;
                });
                add.Enabled = false;
                footer.Controls.Add(add);
                if (playlist)
                {
                    footer.Controls.Add(ButtonOf("全选", 75, () => { foreach (ListViewItem r in result.Items) r.Checked = true; }));
                    footer.Controls.Add(ButtonOf("取消全选", 96, () => { foreach (ListViewItem r in result.Items) r.Checked = false; }));
                }
                CancellationTokenSource cancel = null;
                dlg.FormClosing += (s, e) =>
                {
                    var pending = cancel;
                    if (pending != null) ThreadPool.QueueUserWorkItem(_ => { try { pending.Cancel(); } catch (ObjectDisposedException) { } });
                };
                Func<Task> analyze = async () =>
                {
                    string target = search ? "ytsearch25:" + input.Text.Trim() : Urls(input.Text).FirstOrDefault();
                    if (target == null || (search && input.Text.Trim().Length == 0)) return;
                    go.Enabled = false; input.Enabled = false; add.Enabled = false; analyzed = collectionFolder = collectionCover = null;
                    result.Items.Clear(); note.Text = "解析中…";
                    cancel = new CancellationTokenSource();
                    try
                    {
                        var inspection = await AiRepair.Inspect(options, target, playlist, false, false, cancel.Token, null,
                            text => { if (!dlg.IsDisposed) note.Text = text; }, RunDiagnostics(null));
                        if (dlg.IsDisposed || cancel.IsCancellationRequested) return;
                        var info = inspection.Info;
                        target = analyzed = inspection.Url;
                        if (playlist && !search)
                        {
                            string name = Engine.CollectionDirectoryName(info);
                            if (name != null)
                            {
                                collectionFolder = Path.Combine(options.Downloads, name);
                                collectionCover = Engine.ThumbnailUrl(info);
                            }
                        }
                        if (playlist)
                        {
                            var entries = Engine.Rows(info, "entries").ToArray();
                            if (entries.Length == 0 && !search) entries = new[] { info };
                            foreach (var entry in entries)
                            {
                                string url = Urls(Engine.Value(entry, "webpage_url")).FirstOrDefault() ??
                                    Urls(Engine.Value(entry, "url")).FirstOrDefault();
                                if (url == null && Engine.Value(entry, "ie_key").Equals("Youtube", StringComparison.OrdinalIgnoreCase))
                                    url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(Engine.Value(entry, "id"));
                                if (entries.Length == 1 && !search && !info.ContainsKey("entries")) url = target;
                                if (url != null)
                                    result.Items.Add(new ListViewItem(new[] { Engine.Value(entry, "title"), Engine.Value(entry, "duration"),
                                        new Uri(url).Host }) { Tag = url });
                            }
                        }
                        else
                        {
                            foreach (var item in Engine.Rows(info, "formats").Reverse())
                            {
                                string id = Engine.Value(item, "format_id");
                                string video = Engine.Value(item, "vcodec"), audio = Engine.Value(item, "acodec");
                                if (id.Length == 0 || (video == "none" && audio == "none")) continue;
                                double size;
                                Double.TryParse(Engine.Value(item, "filesize"), out size);
                                result.Items.Add(new ListViewItem(new[] { id, Engine.Value(item, "ext"),
                                    Engine.Value(item, "resolution") + " " + Engine.Value(item, "fps"),
                                    video + " / " + audio, size > 0 ? SizeText(size) : "未知" }) {
                                    Tag = video != "none" && audio == "none" ? id + "+ba/" + id : id
                                });
                            }
                            if (result.Items.Count > 0) result.Items[0].Selected = true;
                        }
                        note.Text = (inspection.Repaired ? "AI 修复成功  ·  " : "") + Engine.Value(info, "title") + "  ·  " + result.Items.Count + " 项";
                        add.Enabled = result.Items.Count > 0;
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (!dlg.IsDisposed)
                        {
                            note.Text = "解析失败";
                            RecordAnalysisFailure(ex, target);
                            MessageBox.Show(dlg, ex.Message + Environment.NewLine + Environment.NewLine +
                                "处理建议 · " + AnalysisAdvice(ex, target), "解析失败");
                        }
                    }
                    finally
                    {
                        cancel.Dispose(); cancel = null;
                        if (!dlg.IsDisposed) { go.Enabled = true; input.Enabled = true; }
                    }
                };
                go.Click += async (s, e) => await analyze();
                dlg.AcceptButton = go;
                if (initial != null) dlg.Shown += async (s, e) => await analyze();
                dlg.Controls.Add(result); dlg.Controls.Add(note); dlg.Controls.Add(top); dlg.Controls.Add(footer);
                ShowAppDialog(dlg, this);
            }
        }

        private async void SupportedSites()
        {
            try
            {
                string sites = await Engine.Capture(Engine.Downloader(), Engine.DownloaderArgs().Concat(new[] { "--list-extractors" }), CancellationToken.None);
                using (var dlg = Dialog("下载组件支持的站点", 650, 500))
                {
                    dlg.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = sites });
                    ShowAppDialog(dlg, this);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法读取站点列表"); }
        }

        private void RenameJob(MediaTask j)
        {
            if (!File.Exists(j.File)) { MessageBox.Show(this, "找不到输出文件。"); return; }
            using (var dlg = Dialog("重命名", 490, 112))
            {
                var input = new TextBox { Dock = DockStyle.Top, Text = Path.GetFileNameWithoutExtension(j.File) };
                Material.Field(input);
                var footer = Footer(); footer.Controls.Add(CloseButton(dlg));
                var save = ButtonOf("确定", 78, () =>
                {
                    try
                    {
                        string name = input.Text.Trim();
                        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith("."))
                            throw new ArgumentException("请输入有效的文件名。");
                        string previous = j.File;
                        string target = Path.Combine(Path.GetDirectoryName(previous), name + Path.GetExtension(previous));
                        if (target != previous) File.Move(previous, target);
                        j.Files = (j.Files ?? new[] { previous }).Select(p => p == previous ? target : p).ToArray();
                        j.File = target; j.Title = name; Changed(); Persist();
                        dlg.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MessageBox.Show(dlg, ex.Message, "重命名失败"); }
                });
                footer.Controls.Add(save); dlg.AcceptButton = save;
                dlg.Controls.Add(input); dlg.Controls.Add(footer); ShowAppDialog(dlg, this);
            }
        }

        private void SetSubtitle(MediaTask j)
        {
            using (var dlg = Dialog("设置转换字幕", 545, 160))
            {
                var form = new SettingsPage("字幕");
                var file = new TextBox { Text = j.Subtitle ?? "" };
                form.Add("字幕文件", file, ButtonOf("浏览", 67, () =>
                {
                    using (var pick = new OpenFileDialog { Filter = "字幕|*.srt;*.ass;*.ssa;*.vtt|所有文件|*.*" })
                        if (pick.ShowDialog(dlg) == DialogResult.OK) file.Text = pick.FileName;
                }));
                var hard = form.Check("硬压字幕到视频画面", j.BurnSubtitle);
                foreach (Control control in form.Tab.Controls.Cast<Control>().ToArray()) dlg.Controls.Add(control);
                var footer = Footer(); footer.Controls.Add(CloseButton(dlg));
                footer.Controls.Add(ButtonOf("移除字幕", 90, () => { j.Subtitle = null; j.BurnSubtitle = false; Changed(); dlg.Close(); }));
                footer.Controls.Add(ButtonOf("确定", 75, () =>
                {
                    if (!File.Exists(file.Text)) { MessageBox.Show(dlg, "请选择字幕文件。"); return; }
                    j.Subtitle = file.Text; j.BurnSubtitle = hard.Checked; Changed(); dlg.Close();
                }));
                dlg.Controls.Add(footer); ShowAppDialog(dlg, this);
            }
        }

        private async void Watermark(MediaTask j)
        {
            string file = File.Exists(j.File) ? j.File : j.Source;
            if (!File.Exists(file)) return;
            try
            {
                var media = await Engine.Probe(file, CancellationToken.None);
                var video = Engine.Rows(media, "streams").FirstOrDefault(row => Engine.Value(row, "codec_type") == "video");
                if (video == null) throw new InvalidOperationException("此文件没有视频画面。");
                int width = Int32.Parse(Engine.Value(video, "width")), height = Int32.Parse(Engine.Value(video, "height"));
                using (var dlg = Dialog("设置水印区域", 700, 490))
                {
                    var preview = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 37, 43), SizeMode = PictureBoxSizeMode.Zoom };
                    var footer = Footer(); footer.Controls.Add(CloseButton(dlg));
                    Rectangle selection = Rectangle.Empty; Point anchor = Point.Empty; bool drawing = false;
                    Func<Rectangle> frame = () =>
                    {
                        double scale = Math.Min((double)preview.ClientSize.Width / width, (double)preview.ClientSize.Height / height);
                        int w = (int)(width * scale), h = (int)(height * scale);
                        return new Rectangle((preview.Width - w) / 2, (preview.Height - h) / 2, w, h);
                    };
                    preview.MouseDown += (s, e) => { if (preview.Image != null && frame().Contains(e.Location)) { drawing = true; anchor = e.Location; selection = Rectangle.Empty; preview.Invalidate(); } };
                    preview.MouseMove += (s, e) =>
                    {
                        if (!drawing) return;
                        var rect = Rectangle.FromLTRB(Math.Min(anchor.X, e.X), Math.Min(anchor.Y, e.Y), Math.Max(anchor.X, e.X), Math.Max(anchor.Y, e.Y));
                        selection = Rectangle.Intersect(rect, frame());
                        preview.Invalidate();
                    };
                    preview.MouseUp += (s, e) => drawing = false;
                    preview.Resize += (s, e) => { selection = Rectangle.Empty; preview.Invalidate(); };
                    preview.Paint += (s, e) =>
                    {
                        using (var pen = new Pen(Color.Gold, 2)) if (!selection.IsEmpty) e.Graphics.DrawRectangle(pen, selection);
                    };
                    var run = ButtonOf("擦除", 78, () =>
                    {
                        var f = frame();
                        if (selection.Width < 5 || selection.Height < 5 || f.Width < 1 || f.Height < 1) { MessageBox.Show(dlg, "请在预览上拖出水印矩形。"); return; }
                        int x = Math.Max(2, (selection.Left - f.Left) * width / f.Width);
                        int y = Math.Max(2, (selection.Top - f.Top) * height / f.Height);
                        int w = Math.Min(width - x - 2, selection.Width * width / f.Width);
                        int h = Math.Min(height - y - 2, selection.Height * height / f.Height);
                        if (w < 2 || h < 2) return;
                        AddLocal(new[] { file }, String.Format("x={0}:y={1}:w={2}:h={3}", x, y, w, h), true);
                        dlg.Close();
                    });
                    footer.Controls.Add(run);
                    var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 41, Padding = new Padding(6) };
                    top.Controls.Add(new Label { Text = "预览位置（秒）", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
                    var time = new NumericUpDown { Minimum = 0, Maximum = 86400, DecimalPlaces = 1, Width = 88, Margin = new Padding(5, 6, 4, 0) };
                    top.Controls.Add(time);
                    var refresh = ButtonOf("获取预览", 95, null); top.Controls.Add(refresh);
                    top.Controls.Add(new Label { Text = "在预览画面上拖出矩形区域", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
                    Func<Task> capture = async () =>
                    {
                        string temp = Path.Combine(Engine.Data, Guid.NewGuid().ToString("N") + ".png");
                        refresh.Enabled = false;
                        try
                        {
                            await Engine.Capture(Engine.FFmpeg(), new[] { "-v", "error", "-y", "-ss",
                                time.Value.ToString(CultureInfo.InvariantCulture), "-i", file, "-frames:v", "1", "-vf", "scale=720:-1", temp }, CancellationToken.None);
                            if (dlg.IsDisposed) return;
                            if (preview.Image != null) preview.Image.Dispose();
                            using (var img = Image.FromFile(temp)) preview.Image = new Bitmap(img);
                            selection = Rectangle.Empty;
                            preview.Invalidate();
                        }
                        catch (Exception ex) { if (!dlg.IsDisposed) MessageBox.Show(dlg, ex.Message, "预览失败"); }
                        finally { Engine.TryDelete(temp); if (!dlg.IsDisposed) refresh.Enabled = true; }
                    };
                    refresh.Click += async (s, e) => await capture();
                    dlg.Shown += async (s, e) => await capture();
                    dlg.Controls.Add(preview); dlg.Controls.Add(top); dlg.Controls.Add(footer);
                    ShowAppDialog(dlg, this);
                    if (preview.Image != null) preview.Image.Dispose();
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法读取视频"); }
        }
    }
}
