using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private bool scheduling;
        private readonly HashSet<MediaTask> resumeAfterPause = new HashSet<MediaTask>();
        private readonly Dictionary<string, string> videoPasswords = new Dictionary<string, string>();

        private static string ClipboardText()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch (System.Runtime.InteropServices.ExternalException) { return ""; }
        }

        private static IEnumerable<string> Urls(string text)
        {
            foreach (Match match in Regex.Matches(text ?? "", @"https?://[^\s<>""“”]+", RegexOptions.IgnoreCase))
            {
                string url = match.Value.TrimEnd('。', '，', '、', '！', '；', '）', '】');
                Uri uri;
                if (Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Host.Length > 0) yield return url;
            }
        }

        private void Paste(string kind)
        {
            string[] urls = Urls(ClipboardText()).Distinct().ToArray();
            if (urls.Length == 0) UrlDialog("", kind, false);
            else if (advanced && kind == "视频")
            { OpenComposer(AutoLink, String.Join(Environment.NewLine, urls)); AnalyzeComposer(); }
            else AddUrls(String.Join("\n", urls), kind, options.Playlist, null, 0);
        }

        private int AddUrls(string input, string kind, bool playlist, string exact, int start, bool duplicate = false,
            DownloadPlan plan = null, string folder = null, string password = null, IDictionary<string, string> titles = null,
            string playlistDirectoryName = null, string collectionCoverUrl = null)
        {
            int added = 0;
            string planKey = Engine.Json().Serialize(plan);
            foreach (string url in Urls(input).Distinct(StringComparer.Ordinal))
            {
                string target = folder ?? options.Downloads;
                if (!duplicate && jobs.Any(j => j.Source == url && j.Kind == kind && j.Playlist == playlist && j.SelectedFormat == exact &&
                    j.Folder == target && Engine.Json().Serialize(j.DownloadOptions) == planKey)) continue;
                string title;
                if (titles == null || !titles.TryGetValue(url, out title)) title = url;
                var job = new MediaTask {
                    Source = url, Title = title, Kind = kind, Playlist = playlist, SelectedFormat = exact, DownloadOptions = plan,
                    Folder = target, PlaylistDirectoryName = playlistDirectoryName, CollectionCoverUrl = collectionCoverUrl,
                    State = options.AutoStart ? "等待中" : "已暂停",
                    Prefix = duplicate ? "重新下载 " + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + " " :
                        start > 0 ? (start + added).ToString("D3") + " " : ""
                };
                jobs.Add(job);
                if (!String.IsNullOrEmpty(password)) videoPasswords[job.Id] = password;
                added++;
            }
            if (added == 0)
            {
                string feedback = "没有新增任务。请检查链接是否有效或已存在。";
                if (page == DownloadPage) choiceStatus.Text = feedback;
                else status.Text = feedback;
                return 0;
            }
            SetPage(DownloadPage); Changed(); Schedule();
            return added;
        }

        private void SetupDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += (s, e) =>
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent(DataFormats.UnicodeText) ||
                    e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("UniformResourceLocatorW") ?
                    DragDropEffects.Copy : DragDropEffects.None;
            };
            control.DragDrop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    var shortcuts = files.Where(p => Path.GetExtension(p).Equals(".url", StringComparison.OrdinalIgnoreCase)).ToArray();
                    var urls = new List<string>();
                    foreach (string p in shortcuts)
                        try { urls.AddRange(File.ReadLines(p).Where(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))); }
                        catch (IOException) { }
                    if (urls.Count > 0) AddUrls(String.Join("\n", urls), "视频", options.Playlist, null, 0);
                    if (files.Length > shortcuts.Length) AddLocal(files.Except(shortcuts), null, false);
                }
                else
                {
                    var text = e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string;
                    if (text == null)
                        using (var stream = e.Data.GetData("UniformResourceLocatorW") as MemoryStream)
                            if (stream != null) text = System.Text.Encoding.Unicode.GetString(stream.ToArray()).TrimEnd('\0');
                    AddUrls(text, "视频", options.Playlist, null, 0);
                }
            };
        }

        private const string MediaFilter = "媒体文件|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv;*.wmv;*.mpg;*.mts;*.m4v;*.ts;*.mp3;*.m4a;*.aac;*.flac;*.wav;*.ogg;*.opus|所有文件|*.*";
        private static readonly string[] ConversionFormats = { "mp4", "mkv", "mov", "webm", "avi", "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus" };

        private void ImportMedia()
        {
            using (var dlg = new OpenFileDialog { Multiselect = true, Title = "导入媒体文件", Filter = MediaFilter })
                if (dlg.ShowDialog(this) == DialogResult.OK) AddLocal(dlg.FileNames, null, false);
        }

        private void AddLocal(IEnumerable<string> files, string region, bool start)
        {
            foreach (string path in files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
                jobs.Add(new MediaTask {
                    Source = path, Title = Path.GetFileName(path), Kind = region == null ? "转换" : "水印擦除",
                    Format = region == null ? conversionFormat : "mp4", Folder = options.Converted, Region = region,
                    CopyStreams = region == null && streamCopy, TranscodeOptions = region == null ? options.Transcode : null, State = start ? "等待中" : "已暂停"
                });
            SetPage(2); Changed(); Schedule();
        }

        private void ResumeJobs(IEnumerable<MediaTask> items)
        {
            foreach (var j in items.Where(j => j.State != "已完成"))
            {
                if (active.ContainsKey(j))
                {
                    if (IsPausing(j)) resumeAfterPause.Add(j);
                    continue;
                }
                j.State = "等待中"; j.Detail = null;
            }
            Changed(); Schedule();
        }

        private static bool IsPausing(MediaTask task) { return task.State == "正在暂停"; }

        private void PauseJobs(IEnumerable<MediaTask> items)
        {
            foreach (var j in items.Where(j => j.State == "等待中" || active.ContainsKey(j)))
            {
                resumeAfterPause.Remove(j);
                j.Speed = 0;
                CancellationTokenSource cts;
                if (!active.TryGetValue(j, out cts))
                {
                    j.State = "已暂停";
                    continue;
                }
                if (IsPausing(j)) continue;
                j.State = "正在暂停";
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { cts.Cancel(); } catch (ObjectDisposedException) { }
                });
            }
            Changed();
        }

        private void Schedule()
        {
            if (quitting || scheduling) return;
            scheduling = true;
            try
            {
                int downloads = Math.Max(1, Math.Min(8, options.Parallel));
                int conversions = Math.Max(1, Math.Min(8, options.ParallelConversions));
                foreach (var job in jobs.Where(j => j.State == "等待中").ToArray())
                {
                    if (active.ContainsKey(job)) continue;
                    int running = active.Keys.Count(task => task.Local == job.Local);
                    if (running < (job.Local ? conversions : downloads)) RunJob(job);
                }
            }
            finally { scheduling = false; }
        }

        private async void RunJob(MediaTask j)
        {
            var cts = new CancellationTokenSource();
            active.Add(j, cts);
            j.State = j.Local ? "转换中" : "解析中";
            j.Detail = null;
            Options snapshot = Engine.Json().Deserialize<Options>(Engine.Json().Serialize(options));
            if (j.DownloadOptions != null) j.DownloadOptions.Apply(snapshot);
            string password;
            videoPasswords.TryGetValue(j.Id, out password);
            string operation = j.Local ? "媒体处理" : "下载";
            RecordRunLog("开始" + operation, task: j);
            try
            {
                Action<double, double, string> update = (percent, speed, detail) => Post(() =>
                {
                    CancellationTokenSource current;
                    if (!active.TryGetValue(j, out current) || current != cts || IsPausing(j)) return;
                    j.State = j.Local ? "转换中" : detail == Engine.PostprocessingDetail ? "处理文件" :
                        detail == Engine.RefreshingDownloadDetail ? "刷新地址" : "下载中";
                    if (detail != Engine.RefreshingDownloadDetail) j.Percent = percent;
                    j.Speed = speed; j.Detail = detail;
                });
                string[] files = j.Local
                    ? await Engine.ConvertMedia(j, cts.Token, update)
                    : await Engine.Download(snapshot, j, cts.Token, update, title => Post(() =>
                    {
                        CancellationTokenSource current;
                        if (!active.TryGetValue(j, out current) || current != cts || IsPausing(j)) return;
                        if (!j.Playlist) j.Title = title;
                        if (j.State == "解析中") j.State = "下载中";
                    }), password, RunDiagnostics(password, j));
                videoPasswords.Remove(j.Id);
                j.File = files[0]; j.Files = files;
                if (j.Title == j.Source || j.Playlist)
                    j.Title = Path.GetFileNameWithoutExtension(j.File) + (files.Length > 1 ? " 等 " + files.Length + " 个文件" : "");
                j.State = "已完成"; j.Percent = 100; j.Detail = null;
                RecordRunLog(operation + "完成", task: j);
                if (options.Notify && !quitting) tray.ShowBalloonTip(2500, "任务完成", j.Title, ToolTipIcon.Info);
            }
            catch (OperationCanceledException)
            {
                j.State = "已暂停"; j.Detail = null;
                RecordRunLog(operation + "已暂停", task: j);
            }
            catch (Exception ex)
            {
                j.State = "失败"; j.Detail = String.IsNullOrEmpty(password) ? ex.Message : ex.Message.Replace(password, "[视频密码]");
                string advice = TaskFailureAdvice(j, ex);
                RecordRunLog(operation + "失败 · " + ex.Message, password, j);
                RecordRunLog("处理建议 · " + advice, password, j);
                j.Detail += Environment.NewLine + advice;
            }
            finally
            {
                j.Speed = 0;
                active.Remove(j);
                if (resumeAfterPause.Remove(j) && !quitting && jobs.Contains(j) && j.State == "已暂停")
                    j.State = "等待中";
                cts.Dispose();
                Changed();
                Schedule();
                if (quitting && active.Count == 0) Close();
            }
        }

        private void Post(Action action)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private void Tick()
        {
            if (options.Watch)
            {
                string value = ClipboardText();
                if (value != lastClipboard)
                {
                    lastClipboard = value;
                    if (Urls(value).Any()) AddUrls(value, "视频", options.Playlist, null, 0);
                }
            }
            if (active.Count > 0) RefreshTasks();
            UpdateTray();
            if (dirty) Persist();
        }

        private void Changed() { dirty = true; RefreshTasks(); }
        private void Persist()
        {
            if (smokeTest) return;
            try { Engine.Save("tasks.json", jobs); dirty = false; }
            catch (Exception ex)
            {
                string feedback = "任务保存失败 · " + ex.Message;
                if (page == DownloadPage) choiceStatus.Text = feedback;
                else status.Text = feedback;
            }
        }
        private void SaveOptions()
        {
            if (smokeTest) return;
            try { Engine.Save("settings.json", options); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置保存失败"); }
        }

        private void RequestExit()
        {
            forceExit = true;
            try { Close(); }
            finally { forceExit = false; }
        }

        private void ClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (confirmingExit || (quitting && active.Count > 0))
                {
                    e.Cancel = true;
                    return;
                }
                if (!quitting)
                {
                    if (options.Tray && !forceExit)
                    {
                        HideToTray();
                        e.Cancel = true;
                        return;
                    }

                    DialogResult decision;
                    confirmingExit = true;
                    try { decision = ConfirmExit(); }
                    finally { confirmingExit = false; }
                    if (decision != DialogResult.OK)
                    {
                        e.Cancel = true;
                        if (decision == DialogResult.Ignore) HideToTray();
                        return;
                    }

                    quitting = true;
                    if (active.Count > 0)
                    {
                        e.Cancel = true;
                        PauseJobs(active.Keys.ToArray());
                        return;
                    }
                }
            }
            if (dirty) Persist();
        }

        private ContextMenuStrip TaskMenu()
        {
            var row = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag;
            var folder = row as CompletedFolder;
            if (folder != null) return BuildMenu(Item("进入合集", () => NavigateCompletedFolder(folder.Path)),
                Item("打开磁盘文件夹", () => OpenFolder(folder.Path)));
            var file = row as CompletedFile;
            if (file != null) return BuildMenu(Item("播放 / 打开文件", () => OpenTaskRow(file)),
                Item("打开磁盘文件夹", () => OpenFolder(Path.GetDirectoryName(file.Path))));
            var selection = Selected(); var j = selection.FirstOrDefault();
            if (j == null) return BuildMenu(Item("打开输出文件夹", () => OpenFolder(page == 2 ? options.Converted : options.Downloads)),
                Item("重试所有失败任务", () => ResumeJobs(VisibleJobs().Where(x => x.State == "失败").ToArray())),
                Item("清除失败任务", () => DeleteJobs(VisibleJobs().Where(x => x.State == "失败").ToArray())));
            var items = new List<ToolStripItem>();
            if (j.State != "已完成")
            {
                items.Add(Item("开始 / 重试", () => ResumeJobs(selection)));
                if (selection.Any(x => x.State == "失败"))
                    items.Add(Item("重试所选失败任务", () => ResumeJobs(selection.Where(x => x.State == "失败"))));
                items.Add(Item("暂停", () => PauseJobs(selection)));
                items.Add(Item("任务详情", () => MessageBox.Show(this, j.Detail ?? j.Source, j.State)));
                items.Add(Item("查看任务日志", () => ShowTaskRunLog(j)));
                if (j.State == "等待中")
                {
                    items.Add(Item("移到队首", () => MoveWaitingJob(j, 0)));
                    items.Add(Item("上移", () => MoveWaitingJob(j, -1)));
                    items.Add(Item("下移", () => MoveWaitingJob(j, 1)));
                }
            }
            if (j.Local || j.State == "已完成")
            {
                items.Add(Item("播放 / 打开文件", () => OpenTask(j)));
                if (j.Local && File.Exists(j.Source)) items.Add(Item("查看源媒体信息", () => ShowSourceMedia(j)));
                items.Add(Item("打开文件夹", () => OpenFolder(j.File != null ? Path.GetDirectoryName(j.File) : j.Folder)));
                if (j.State == "已完成" && !j.Local)
                {
                    items.Add(Item("加入转换列表", () => AddLocal(selection.SelectMany(x => x.Files ?? new[] { x.File }), null, false)));
                    items.Add(Item("重命名", () => RenameJob(j)));
                    items.Add(Item("获取字幕", () => UrlDialog(j.Source, "字幕", false)));
                    items.Add(Item("获取封面", () => AddUrls(j.Source, "封面", j.Playlist, null, 0)));
                }
                if (j.Local || j.Kind == "视频") items.Add(Item("水印擦除", () => Watermark(j)));
            }
            if (j.Kind == "转换" && j.State != "已完成" && !active.ContainsKey(j))
                items.Add(Item("添加或移除字幕", () => SetSubtitle(j)));
            if (!j.Local)
            {
                items.Add(Item("复制 URL", () => Copy(j.Source)));
                items.Add(Item("复制标题", () => Copy(j.Title ?? j.Source)));
                if (j.State == "已完成") items.Add(Item("重新下载", () => AddUrls(j.Source, j.Kind, j.Playlist, j.SelectedFormat, 0, true, j.DownloadOptions, j.Folder, playlistDirectoryName: j.PlaylistDirectoryName,
                    collectionCoverUrl: j.CollectionCoverUrl)));
                items.Add(Item("来源页面", () => ShellOpen(j.Source)));
            }
            items.Add(new ToolStripSeparator());
            items.Add(Item("删除任务", () => DeleteJobs(selection)));
            items.Add(Item("清空当前列表", () => DeleteJobs(VisibleJobs().ToArray())));
            return BuildMenu(items.ToArray());
        }

        private void MoveWaitingJob(MediaTask job, int direction)
        {
            var waiting = jobs.Where(item => item.Local == job.Local && item.State == "等待中").ToArray();
            int index = Array.IndexOf(waiting, job);
            int target = direction == 0 ? 0 : index + direction;
            if (index < 0 || target < 0 || target >= waiting.Length || index == target) return;
            int position = jobs.IndexOf(waiting[target]);
            jobs.Remove(job);
            jobs.Insert(position, job);
            Changed();
            Schedule();
        }

        private bool Copy(string value)
        {
            try { Clipboard.SetText(value); lastClipboard = value; return true; }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                MessageBox.Show(this, ex.Message, "无法访问剪贴板");
                return false;
            }
        }

        private void DeleteJobs(MediaTask[] items)
        {
            if (items.Length == 0) return;
            if (MessageBox.Show(this, "从列表移除 " + items.Length + " 个任务？媒体文件会保留。", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            PauseJobs(items);
            foreach (var item in items) { jobs.Remove(item); videoPasswords.Remove(item.Id); }
            Changed(); Persist();
        }

        private void ShellOpen(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开"); }
        }

        private void OpenFolder(string path)
        {
            try { Directory.CreateDirectory(path); ShellOpen(path); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开文件夹"); }
        }

        private void OpenTask(MediaTask j)
        {
            if (File.Exists(j.File)) ShellOpen(j.File);
            else if (j.Local && File.Exists(j.Source)) ShellOpen(j.Source);
            else MessageBox.Show(this, j.Detail ?? j.Source, j.State);
        }

        private void ExportTasks()
        {
            using (var dlg = new SaveFileDialog { FileName = "TubeGet tasks.json", Filter = "TubeGet 任务|*.json" })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    try { File.WriteAllText(dlg.FileName, Engine.Json().Serialize(jobs)); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
        }

        private void ImportTasks()
        {
            using (var dlg = new OpenFileDialog { Filter = "TubeGet 任务|*.json" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var imported = Engine.Json().Deserialize<List<MediaTask>>(File.ReadAllText(dlg.FileName));
                    if (imported == null) throw new InvalidDataException("文件没有可读取的任务列表。");
                    var accepted = new List<MediaTask>();
                    int invalid = 0, duplicates = 0;
                    foreach (var old in imported)
                    {
                        if (old == null || !(old.Local ? File.Exists(old.Source) : Urls(old.Source).Contains(old.Source)))
                        {
                            invalid++;
                            continue;
                        }
                        string kind = old.Local ? "转换" : new[] { "视频", "音频", "封面", "字幕", "字幕与封面" }.Contains(old.Kind) ? old.Kind : "视频";
                        if (jobs.Concat(accepted).Any(j => j.Source == old.Source && j.Kind == kind && j.Playlist == old.Playlist))
                        {
                            duplicates++;
                            continue;
                        }
                        accepted.Add(new MediaTask {
                            Source = old.Source, Title = old.Title,
                            Kind = kind,
                            Playlist = old.Playlist, Folder = old.Local ? options.Converted : options.Downloads,
                            Format = ConversionFormats.Contains(old.Format) ? old.Format : "mp4",
                            CopyStreams = old.CopyStreams, State = "已暂停"
                        });
                    }
                    string summary = "可导入 " + accepted.Count + " 项 · 重复 " + duplicates + " 项 · 无效 " + invalid + " 项";
                    if (accepted.Count == 0)
                    {
                        MessageBox.Show(this, summary, "导入任务");
                        return;
                    }
                    if (MessageBox.Show(this, summary + "\n导入后以暂停状态加入队列。", "确认导入",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                    jobs.AddRange(accepted);
                    Changed(); Persist();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "导入失败"); }
            }
        }
    }
}
