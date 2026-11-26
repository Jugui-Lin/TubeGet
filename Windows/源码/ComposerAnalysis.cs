using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private void AnalyzeComposer() { AnalyzeComposer(false); }

        private async void AnalyzeComposer(bool importedList, string retryInput = null)
        {
            if (analysis != null)
            {
                CancelComposerAnalysis();
                UpdateComposerSelection();
                choiceStatus.Text = "已取消解析";
                return;
            }
            string input = retryInput ?? links.Text;
            string[] urls = Urls(input).Distinct().ToArray();
            if (urls.Length == 0) { choiceStatus.Text = "请输入有效的链接"; return; }
            int mode = composerMode;
            if (mode == AutoLink && (urls.Length > 1 || importedList)) mode = TextLinks;
            if (mode != TextLinks && urls.Length > 1)
            { choiceStatus.Text = "此模式只处理一个链接，多条链接可使用「自动识别」"; return; }
            ResetComposerResult();
            if (links.TextLength == 0) clearedComposerInput = input;
            if (mode == TextLinks)
            {
                ConfigureComposerResult(TextLinks);
                analyzedUrl = urls[0];
                AddCollectionRows(urls.Select(url => new Entry { Url = url, Title = url }));
                SetBatchSubtitleChoices();
                RecordRunLog("已识别 " + choices.Items.Count + " 条链接");
                return;
            }

            string password = composerPassword;
            var request = new CancellationTokenSource();
            analysis = request;
            SetAnalysisButtonState(true);
            int generation = analysisGeneration;
            clearListButton.Enabled = true;
            choiceStatus.Text = "正在解析…";
            RecordRunLog("开始解析 · " + new Uri(urls[0]).Host);
            try
            {
                var snapshot = Engine.Json().Deserialize<Options>(Engine.Json().Serialize(options));
                new DownloadPlan { Cookies = composerCookies, CookieBrowser = composerCookieBrowser }.Apply(snapshot);
                if (CookieSessions.Site(new Uri(urls[0])) == "xinpianchang.com" &&
                    CookieSessions.IsSavedPath(Engine.CookieFileFor(snapshot, urls[0])))
                    RecordRunLog("登录会话 · 已选用新片场的应用内登录会话");
                var inspection = await AiRepair.Inspect(snapshot, urls[0], mode == AutoLink || mode == CollectionLinks,
                    mode == AutoLink, mode == Sidecars, request.Token, password, text =>
                    {
                        if (generation == analysisGeneration && !request.IsCancellationRequested && !IsDisposed)
                            choiceStatus.Text = text;
                    }, RunDiagnostics(password));
                if (generation != analysisGeneration || request.IsCancellationRequested || IsDisposed) return;
                var info = inspection.Info;
                if (!AiRepair.IsUsable(info, mode == Sidecars))
                    throw new InvalidDataException(mode == Sidecars ? "未找到可用的字幕或封面" :
                        Engine.IsCollectionResult(info) ? "未找到可用的视频条目" : "未找到可用的视频或音频格式");
                if (mode == AutoLink) mode = Engine.IsCollectionResult(info) ? CollectionLinks : SingleLink;
                ConfigureComposerResult(mode);
                analyzedUrl = inspection.Url;
                resultPassword = password;
                var title = Engine.Value(info, "title");
                string imageUrl = Engine.ThumbnailUrl(info);
                if (mode == CollectionLinks)
                {
                    collectionDirectoryName = Engine.CollectionDirectoryName(info);
                    collectionCoverUrl = collectionDirectoryName == null ? null : imageUrl;
                    var entries = CollectionLeaves(info).Select(item => new Entry {
                        Url = CollectionUrl(item), Title = Engine.Value(item, "title"), Format = DurationText(Engine.Value(item, "duration")),
                        Thumbnail = Engine.ThumbnailUrl(item)
                    }).Where(item => item.Url != null).ToList();
                    if (entries.Count == 0 && collectionDirectoryName == null)
                        entries.Add(new Entry { Url = analyzedUrl, Title = title, Format = DurationText(Engine.Value(info, "duration")) });
                    if (imageUrl.Length == 0)
                        imageUrl = entries.Select(entry => entry.Thumbnail).FirstOrDefault(value => !String.IsNullOrEmpty(value)) ?? "";
                    collectionPreviewSession = new CancellationTokenSource();
                    ShowMediaMetadata(info, analyzedUrl, entries.Count);
                    AddCollectionRows(entries);
                    SetBatchSubtitleChoices();
                }
                else
                {
                    ShowMediaMetadata(info, analyzedUrl);
                    PopulateComposerFormats(info, mode == Sidecars);
                    SetSubtitleChoices(info, mode == Sidecars);
                    choiceStatus.Text = choices.Items.Count == 0 && mode != Sidecars
                        ? "没有可下载的画质" : mode == Sidecars ? "选择字幕语言或保存封面" :
                        choices.SelectedItems.Count == 0 ? "选择画质或仅音频" : SelectedFormatStatus();
                }
                if ((mode == CollectionLinks || mode == SingleLink) && choices.Items.Count == 0)
                    throw new InvalidDataException(mode == CollectionLinks ? "未找到可用的视频条目" : "未找到可用的视频或音频格式");
                UpdateComposerSelection();
                if (inspection.Repaired) choiceStatus.Text = "AI 修复成功 · " + choiceStatus.Text;
                RecordRunLog("解析结束 · " + choiceStatus.Text);
                if (imageUrl.Length > 0)
                {
                    Image image = await ReadPreview(imageUrl, request.Token);
                    if (image != null)
                    {
                        if (generation != analysisGeneration || request.IsCancellationRequested || IsDisposed) image.Dispose();
                        else
                        {
                            preview.Image = image;
                            preview.Visible = true;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                RecordRunLog("解析已取消");
                if (!IsDisposed && generation == analysisGeneration && choiceStatus.Text == "正在解析…") choiceStatus.Text = "已取消解析";
            }
            catch (Exception ex)
            {
                if (!IsDisposed && generation == analysisGeneration && !request.IsCancellationRequested)
                {
                    ResetComposerResult("解析失败");
                    if (links.TextLength == 0) clearedComposerInput = input;
                    RecordAnalysisFailure(ex, urls[0], password);
                    reanalyzeButton.Visible = true;
                }
            }
            finally
            {
                if (analysis == request)
                {
                    analysis = null;
                    if (!IsDisposed)
                    {
                        SetAnalysisButtonState(false);
                        clearListButton.Enabled = analyzedUrl != null || choices.Items.Count > 0;
                    }
                }
                request.Dispose();
            }
        }

        private void ShowMediaMetadata(Dictionary<string, object> info, string url, int? collectionCount = null)
        {
            var uri = new Uri(url);
            mediaTitle.Text = Engine.Value(info, "title");
            if (mediaTitle.Text.Length == 0) mediaTitle.Text = uri.Host;
            string published = Engine.Value(info, "upload_date");
            DateTime date;
            if (DateTime.TryParseExact(published, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                published = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            else published = "";
            summary.Text = String.Join("  ·  ", new[] {
                MediaSourceName(uri), Engine.Value(info, "uploader"), collectionCount.HasValue ? collectionCount.Value + " 项视频" : DurationText(Engine.Value(info, "duration")), published
            }.Where(t => t.Length > 0));
        }

        private static string MediaSourceName(Uri uri)
        {
            switch (CookieSessions.Site(uri))
            {
                case "bilibili.com": return "B 站";
                case "youtube.com": return "YouTube";
                case "xinpianchang.com": return "新片场";
                case "douyin.com": return "抖音";
                case "kuaishou.com": return "快手";
                case "behance.net": return "Behance";
                case "vimeo.com": return "Vimeo";
                default: return uri.Host;
            }
        }

        private static IEnumerable<Dictionary<string, object>> CollectionLeaves(Dictionary<string, object> collection)
        {
            foreach (var item in Engine.Rows(collection, "entries"))
            {
                if (!Engine.IsCollectionResult(item)) yield return item;
                else foreach (var child in CollectionLeaves(item)) yield return child;
            }
        }

        private static string CollectionUrl(Dictionary<string, object> entry)
        {
            string url = Urls(Engine.Value(entry, "webpage_url")).FirstOrDefault() ??
                Urls(Engine.Value(entry, "url")).FirstOrDefault();
            if (url == null && Engine.Value(entry, "ie_key").Equals("Youtube", StringComparison.OrdinalIgnoreCase))
            {
                string id = Engine.Value(entry, "id");
                if (id.Length > 0) url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id);
            }
            return url;
        }

        private void AddCollectionRows(IEnumerable<Entry> entries)
        {
            choices.BeginUpdate();
            updatingRows = true;
            try
            {
                foreach (var entry in entries.GroupBy(item => item.Url, StringComparer.Ordinal).Select(group => group.First()))
                {
                    string source = MediaSourceName(new Uri(entry.Url));
                    var row = new ListViewItem(new[] { String.IsNullOrEmpty(entry.Title) ? entry.Url : entry.Title, source, entry.Format ?? "" }) {
                        Tag = entry, ToolTipText = entry.Url
                    };
                    choices.Items.Add(row);
                    row.Checked = true;
                }
            }
            finally { updatingRows = false; choices.EndUpdate(); }
            UpdateComposerSelection();
        }

        private void PopulateComposerFormats(Dictionary<string, object> info, bool sidecars)
        {
            var formats = Engine.Rows(info, "formats").Reverse().ToArray();
            bool hasAudioTrack = formats.Any(format => Engine.Value(format, "vcodec") == "none" &&
                Engine.Value(format, "acodec") != "none");
            var tracks = new List<string> { "自动选择" };
            var trackIds = new List<string> { null };
            formatSizeColumnWidth = 0;
            choices.BeginUpdate();
            updatingRows = true;
            try
            {
                foreach (var format in formats)
                {
                    string id = Engine.Value(format, "format_id");
                    string video = Engine.Value(format, "vcodec");
                    string audio = Engine.Value(format, "acodec");
                    string resolution = Engine.Value(format, "resolution");
                    string container = Engine.Value(format, "ext");
                    if (id.Length == 0 || container == "mhtml" || (video == "none" && audio == "none")) continue;
                    bool audioOnly = video == "none";
                    if (audioOnly)
                    {
                        string language = Engine.Value(format, "language");
                        tracks.Add(String.Join(" · ", new[] { language, audio, id }.Where(v => v.Length > 0)));
                        trackIds.Add(id);
                    }
                    if (sidecars) continue;
                    var entry = new Entry { Format = id, HasVideo = !audioOnly, NeedsAudio = !audioOnly && audio == "none" };
                    Int32.TryParse(Engine.Value(format, "width"), out entry.Width);
                    Int32.TryParse(Engine.Value(format, "height"), out entry.Height);
                    string fps = Engine.Value(format, "fps");
                    string name = audioOnly ? "仅音频" : QualityLabel(format, resolution.Length == 0 ? container : resolution);
                    string audioLabel = audioOnly ? container.ToUpperInvariant() :
                        entry.NeedsAudio ? hasAudioTrack ? "自动合并音轨" : "仅视频" : "含音轨";
                    entry.FormatDetail = String.Join("  ·  ", new[] {
                        audioLabel, fps.Length == 0 ? "" : fps + " fps", audioOnly ? "" : resolution
                    }.Where(value => value.Length > 0));
                    string size = FormatSize(format);
                    formatSizeColumnWidth = Math.Max(formatSizeColumnWidth, TextRenderer.MeasureText(size, Font,
                        Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Ui(22));
                    choices.Items.Add(new ListViewItem(new[] {
                        name, CodecLabel(audioOnly ? audio : video, container), size
                    }) { Tag = entry, ToolTipText = entry.FormatDetail + Environment.NewLine + "大小 " + size + Environment.NewLine + "格式 " + id });
                }
            }
            finally
            {
                try { choices.EndUpdate(); }
                finally { updatingRows = false; }
            }
            if (!sidecars && choices.Items.Count > 0) choices.Items[0].Selected = true;
            audioIds = trackIds.ToArray();
            audioPicker.SetChoices(tracks.ToArray(), tracks[0]);
            UpdateComposerSelection();
        }

        private static string QualityLabel(Dictionary<string, object> format, string fallback)
        {
            int width, height;
            if (!Int32.TryParse(Engine.Value(format, "width"), out width) ||
                !Int32.TryParse(Engine.Value(format, "height"), out height))
            {
                var dimensions = Regex.Match(fallback, @"^([0-9]+)x([0-9]+)$");
                if (!dimensions.Success || !Int32.TryParse(dimensions.Groups[1].Value, out width) ||
                    !Int32.TryParse(dimensions.Groups[2].Value, out height)) return fallback;
            }
            int shortEdge = Math.Min(width, height);
            string quality = shortEdge + "p";
            switch (shortEdge)
            {
                case 4320: return quality + " · 8K";
                case 2160: return quality + " · 4K";
                case 1440: return quality + " · 2K";
                case 1080: return quality + " · 全高清";
                case 720: return quality + " · 高清";
                default: return quality;
            }
        }

        private static string CodecLabel(string codec, string container)
        {
            string value = codec.ToLowerInvariant();
            if (value.StartsWith("avc", StringComparison.Ordinal) || value == "h264") return "H.264";
            if (value.StartsWith("hev", StringComparison.Ordinal) || value.StartsWith("hvc", StringComparison.Ordinal) ||
                value == "h265" || value == "bytevc1") return "HEVC";
            if (value.StartsWith("av01", StringComparison.Ordinal) || value == "av1") return "AV1";
            if (value.StartsWith("vp09", StringComparison.Ordinal) || value == "vp9") return "VP9";
            if (value.StartsWith("mp4a", StringComparison.Ordinal)) return "AAC";
            return (value.Length == 0 || value == "none" ? container : value).ToUpperInvariant();
        }

        private void SetBatchSubtitleChoices()
        {
            subtitleIds = new[] { null, "", "all", "zh.*", "en.*" };
            automaticSubtitles = new bool[subtitleIds.Length];
            subtitlePicker.SetChoices(new[] { "默认语言", "不下载字幕", "全部人工字幕", "中文字幕", "英文字幕" }, "默认语言");
        }

        private static IEnumerable<string> SubtitleKeys(Dictionary<string, object> info, string key)
        {
            object value;
            var map = info.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
            return map == null ? Enumerable.Empty<string>() : map.Keys.OrderBy(s => s, StringComparer.Ordinal);
        }

        private void SetSubtitleChoices(Dictionary<string, object> info, bool sidecars)
        {
            var names = new List<string>();
            var languages = new List<string>();
            var automatic = new List<bool>();
            if (!sidecars) { names.Add("默认语言"); languages.Add(null); automatic.Add(false); }
            names.Add("不下载字幕"); languages.Add(""); automatic.Add(false);
            var human = SubtitleKeys(info, "subtitles").ToArray();
            foreach (bool auto in new[] { false, true })
            {
                var keys = auto ? SubtitleKeys(info, "automatic_captions").Except(human) : human;
                foreach (string key in keys)
                {
                    string name = key + (auto ? " · 自动字幕" : " · 人工字幕");
                    names.Add(name);
                    languages.Add("^" + Regex.Escape(key) + "$");
                    automatic.Add(auto);
                    if (sidecars) choices.Items.Add(new ListViewItem(new[] { key, auto ? "自动字幕" : "人工字幕", "" }));
                }
            }
            subtitleIds = languages.ToArray();
            automaticSubtitles = automatic.ToArray();
            subtitlePicker.SetChoices(names.ToArray(), names[0]);
        }

        private void SelectSidecarLanguage(object sender, EventArgs args)
        {
            if (resultMode != Sidecars || choices.SelectedIndices.Count == 0) return;
            int selected = choices.SelectedIndices[0] + 1;
            if (selected < subtitleIds.Length) subtitlePicker.Choose(selected);
        }

        private void QueueComposer()
        {
            if (analyzedUrl == null) return;
            try
            {
                string folder = destination.Text.Trim();
                if (!Path.IsPathRooted(folder)) throw new ArgumentException("请输入完整的保存路径");
                folder = Path.GetFullPath(folder);
                var plan = new DownloadPlan {
                    Cookies = composerCookies,
                    CookieBrowser = composerCookieBrowser,
                    Languages = subtitleIds[subtitlePicker.SelectedIndex],
                    AutoCaptions = subtitleIds[subtitlePicker.SelectedIndex] == null ? (bool?)null : automaticSubtitles[subtitlePicker.SelectedIndex],
                    SubtitleFormat = subtitleFormat.SelectedIndex == 0 ? null : subtitleFormat.Text,
                    Cover = coverPicker.Checked
                };
                if (resultMode == Sidecars)
                {
                    bool subtitle = !String.IsNullOrEmpty(plan.Languages);
                    if (!subtitle && !coverPicker.Checked) return;
                    string kind = subtitle ? (coverPicker.Checked ? "字幕与封面" : "字幕") : "封面";
                    ReportQueued(AddUrls(analyzedUrl, kind, false, null, 0, false, plan, folder, resultPassword));
                    return;
                }
                if (resultMode == TextLinks || resultMode == CollectionLinks)
                {
                    var entries = choices.CheckedItems.Cast<ListViewItem>().Select(row => (Entry)row.Tag).ToArray();
                    if (entries.Length == 0) return;
                    plan.Quality = qualityPicker.SelectedIndex == 0 ? null : qualityPicker.Text;
                    if (collectionDirectoryName != null) folder = Path.Combine(folder, collectionDirectoryName);
                    ReportQueued(AddUrls(String.Join("\n", entries.Select(entry => entry.Url)), "视频", false, null,
                        numberPicker.Checked ? 1 : 0, false, plan, folder, null,
                        entries.ToDictionary(entry => entry.Url, entry => entry.Title, StringComparer.Ordinal),
                        playlistDirectoryName: collectionDirectoryName, collectionCoverUrl: collectionCoverUrl));
                    return;
                }
                var selected = choices.SelectedItems.Count == 0 ? null : choices.SelectedItems[0].Tag as Entry;
                if (selected == null) return;
                string audio = audioIds[audioPicker.SelectedIndex];
                string video = Engine.IsXinpianchang(analyzedUrl) && selected.HasVideo
                    ? Engine.ConstrainFormatDimensions(selected.Format, selected.Width, selected.Height) : selected.Format;
                string format = selected.NeedsAudio ? video + (audio == null ? "+ba/" + video : "+" + audio) : video;
                ReportQueued(AddUrls(analyzedUrl, selected.HasVideo ? "视频" : "音频", false, format, 0, false, plan, folder, resultPassword));
            }
            catch (Exception ex) { choiceStatus.Text = SafeComposerError(ex); }
        }

        private void ReportQueued(int added)
        {
            choiceStatus.Text = added > 0 ? "已加入 " + added + " 项下载任务" +
                (collectionDirectoryName == null ? "" : "  ·  " + collectionDirectoryName) : "所选任务已在下载队列中";
        }

        private static Task<Image> ReadPreview(string address, CancellationToken token, Size thumbnailSize = default(Size))
        {
            return Task.Run<Image>(() =>
            {
                Uri url;
                if (token.IsCancellationRequested || !Uri.TryCreate(address, UriKind.Absolute, out url) ||
                    (url.Scheme != "https" && url.Scheme != "http")) return null;
                const int max = 3 * 1024 * 1024;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 4500;
                request.ReadWriteTimeout = 2500;
                request.MaximumAutomaticRedirections = 4;
                try
                {
                    using (token.Register(request.Abort))
                    using (var response = request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    using (var buffer = new MemoryStream())
                    {
                        if (response.ContentLength > max || stream == null) return null;
                        byte[] chunk = new byte[8192];
                        int n;
                        while ((n = stream.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            if (buffer.Length + n > max) return null;
                            buffer.Write(chunk, 0, n);
                        }
                        buffer.Position = 0;
                        using (var image = Image.FromStream(buffer))
                        {
                            if (image.Width > 4096 || image.Height > 4096) return null;
                            if (thumbnailSize.IsEmpty) return new Bitmap(image);
                            float scale = Math.Min(thumbnailSize.Width / (float)image.Width, thumbnailSize.Height / (float)image.Height);
                            var reduced = new Bitmap(Math.Max(1, (int)Math.Round(image.Width * scale)),
                                Math.Max(1, (int)Math.Round(image.Height * scale)));
                            using (var graphics = Graphics.FromImage(reduced))
                            {
                                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                graphics.DrawImage(image, 0, 0, reduced.Width, reduced.Height);
                            }
                            return reduced;
                        }
                    }
                }
                catch (WebException) { return null; }
                catch (IOException) { return null; }
                catch (ArgumentException) { return null; }
                catch (System.Runtime.InteropServices.ExternalException) { return null; }
                catch (ObjectDisposedException) { return null; }
            });
        }
    }
}
