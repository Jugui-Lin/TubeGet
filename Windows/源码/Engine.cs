using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TubeGet
{
    public sealed class Options
    {
        public string Downloads { get; set; }
        public string Converted { get; set; }
        public string Quality { get; set; }
        public string Container { get; set; }
        public string AudioFormat { get; set; }
        public string SubtitleFormat { get; set; }
        public string Languages { get; set; }
        public string Proxy { get; set; }
        public string Rate { get; set; }
        public string Cookies { get; set; }
        public string CookieBrowser { get; set; }
        public string Agent { get; set; }
        public string Referer { get; set; }
        public int Parallel { get; set; }
        public bool Watch { get; set; }
        public bool AutoStart { get; set; }
        public bool Resume { get; set; }
        public bool Playlist { get; set; }
        public bool Cover { get; set; }
        public bool Subtitles { get; set; }
        public bool AutoCaptions { get; set; }
        public bool EmbedSubs { get; set; }
        public bool Metadata { get; set; }
        public bool Aria { get; set; }
        public bool Tray { get; set; }
        public bool Notify { get; set; }
        public string ConvertFormat { get; set; }
        public bool StreamCopy { get; set; }
        public TranscodePlan Transcode { get; set; }
        public string Theme { get; set; }
        public bool CompletedCards { get; set; }
        public bool CompletedFolderCards { get; set; }
        public bool AutoUpdateComponents { get; set; }
        public bool AutoUpdateApp { get; set; }
        public AiRepairSettings AiRepair { get; set; }

        public Options()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Downloads = Path.Combine(home, "Downloads", "TubeGet Downloaded");
            Converted = Path.Combine(home, "Downloads", "TubeGet Converted");
            Quality = "最佳"; Container = "默认"; AudioFormat = "mp3"; SubtitleFormat = "srt";
            Languages = "zh.*,en.*"; Parallel = 2; AutoStart = true;
        }
    }

    public sealed class DownloadPlan
    {
        public string Quality { get; set; }
        public string Cookies { get; set; }
        public string CookieBrowser { get; set; }
        public string Languages { get; set; }
        public string SubtitleFormat { get; set; }
        public bool? Cover { get; set; }
        public bool? AutoCaptions { get; set; }

        internal void Apply(Options options)
        {
            options.Quality = Quality ?? options.Quality;
            if (CookieBrowser != null || Cookies != null)
            {
                options.CookieBrowser = CookieBrowser;
                options.Cookies = Cookies;
            }
            if (Languages != null)
            {
                options.Languages = Languages;
                options.Subtitles = Languages.Length > 0;
            }
            options.SubtitleFormat = SubtitleFormat ?? options.SubtitleFormat;
            options.Cover = Cover ?? options.Cover;
            options.AutoCaptions = AutoCaptions ?? options.AutoCaptions;
        }
    }

    public sealed class MediaTask
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Title { get; set; }
        public string Kind { get; set; }
        public string State { get; set; }
        public string Detail { get; set; }
        public string File { get; set; }
        public string Folder { get; set; }
        public string Format { get; set; }
        public string SelectedFormat { get; set; }
        public DownloadPlan DownloadOptions { get; set; }
        public TranscodePlan TranscodeOptions { get; set; }
        public string Prefix { get; set; }
        public string Subtitle { get; set; }
        public string Region { get; set; }
        public string[] Files { get; set; }
        public bool Playlist { get; set; }
        public string PlaylistDirectoryName { get; set; }
        public string CollectionCoverUrl { get; set; }
        public bool CopyStreams { get; set; }
        public bool BurnSubtitle { get; set; }
        public double Percent { get; set; }
        public double Speed { get; set; }
        public DateTime Added { get; set; }

        public MediaTask() { Id = Guid.NewGuid().ToString("N"); Added = DateTime.Now; State = "等待中"; }
        public bool Local { get { return Kind == "转换" || Kind == "水印擦除"; } }
    }

    internal sealed class MediaAccessException : InvalidOperationException
    {
        internal MediaAccessException(string message) : base(message) { }
    }

    internal static class Engine
    {
        internal static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TubeGetDesktop");
        internal static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        internal static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 192 };
        }

        internal static T Read<T>(string name, T fallback)
        {
            try { return Json().Deserialize<T>(File.ReadAllText(Path.Combine(Data, name), Encoding.UTF8)); }
            catch (Exception) { return fallback; }
        }

        internal static void Save<T>(string name, T data)
        {
            Directory.CreateDirectory(Data);
            string path = Path.Combine(Data, name), temp = path + ".tmp";
            File.WriteAllText(temp, Json().Serialize(data), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        internal static readonly string ComponentDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "components");

        internal static readonly ComponentUpdater ComponentUpdates = new ComponentUpdater(Path.Combine(Data, "component-updates"));

        private static string Tool(string relativePath)
        {
            string path = ComponentUpdates.Resolve(relativePath) ?? Path.Combine(ComponentDirectory, relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到内置组件 " + Path.GetFileName(path) + "。请完整解压 TubeGet 发布包。", path);
            return path;
        }

        internal static string Downloader() { return Tool(@"yt-dlp\yt-dlp.exe"); }
        internal static string FFmpeg() { return Tool(@"ffmpeg\ffmpeg.exe"); }
        private static string ProbeTool() { return Tool(@"ffmpeg\ffprobe.exe"); }
        internal static string JavaScriptRuntime() { return Tool(@"quickjs\qjs.exe"); }
        internal static string Aria2() { return Tool(@"aria2\aria2c.exe"); }

        private static string Quote(string value)
        {
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0) return value;
            var quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') quoted.Append('\\', slashes + 1);
                quoted.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }

        internal static Task<int> Run(string file, IEnumerable<string> args, CancellationToken token, Action<string> stdout, Action<string> stderr,
            string input = null, string workingDirectory = null, IDictionary<string, string> environment = null)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var actual = args.ToList();
                string cookieCopy = null;
                try
                {
                    int cookieArgument = actual.IndexOf("--cookies");
                    if (Path.GetFileName(file).Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase) &&
                        cookieArgument >= 0 && cookieArgument + 1 < actual.Count)
                    {
                        cookieCopy = Path.Combine(Path.GetTempPath(), "tubeget-session-" + Guid.NewGuid().ToString("N") + ".txt");
                        string source = actual[cookieArgument + 1];
                        if (CookieSessions.IsSavedPath(source)) CookieSessions.Open(source, cookieCopy);
                        else File.Copy(source, cookieCopy);
                        actual[cookieArgument + 1] = cookieCopy;
                    }
                    var info = new ProcessStartInfo(file, String.Join(" ", actual.Select(Quote))) {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                        RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = workingDirectory ?? Data,
                        RedirectStandardInput = input != null
                    };
                    info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                    if (environment != null)
                        foreach (var item in environment) info.EnvironmentVariables[item.Key] = item.Value;
                    Directory.CreateDirectory(info.WorkingDirectory);
                    using (var proc = new Process { StartInfo = info })
                    {
                        proc.OutputDataReceived += (s, e) => { if (e.Data != null && stdout != null) stdout(e.Data); };
                        proc.ErrorDataReceived += (s, e) => { if (e.Data != null && stderr != null) stderr(e.Data); };
                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        using (token.Register(() => Stop(proc)))
                        {
                            if (input != null)
                            {
                                try
                                {
                                    using (var stdin = proc.StandardInput)
                                    {
                                        var bytes = Encoding.UTF8.GetBytes(input);
                                        stdin.BaseStream.Write(bytes, 0, bytes.Length);
                                    }
                                }
                                catch (IOException) { token.ThrowIfCancellationRequested(); }
                            }
                            proc.WaitForExit();
                            token.ThrowIfCancellationRequested();
                            return proc.ExitCode;
                        }
                    }
                }
                finally { if (cookieCopy != null) TryDelete(cookieCopy); }
            }, token);
        }

        private static void Stop(Process process)
        {
            try
            {
                if (process.HasExited) return;
                string taskkill = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
                using (var kill = Process.Start(new ProcessStartInfo(taskkill, "/PID " + process.Id + " /T /F") {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
                })) kill.WaitForExit(4000);
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        internal static List<string> DownloaderArgs()
        {
            return new List<string> {
                "--ignore-config", "--no-plugin-dirs", "--plugin-dirs",
                Path.Combine(ComponentDirectory, "yt-dlp", "yt-dlp-plugins")
            };
        }

        private static List<string> WebArgs(Options o, string url, string password)
        {
            var args = DownloaderArgs();
            args.AddRange(new[] { "--no-color", "--encoding", "utf-8",
                "--socket-timeout", "20", "--retries", "3", "--fragment-retries", "5", "--windows-filenames" });
            if (!String.IsNullOrWhiteSpace(o.Proxy)) args.AddRange(new[] { "--proxy", o.Proxy.Trim() });
            string cookieFile = CookieFileFor(o, url);
            if (CookieSessions.IsBrowser(o.CookieBrowser))
                args.AddRange(new[] { "--cookies-from-browser", o.CookieBrowser });
            else if (!String.IsNullOrWhiteSpace(cookieFile))
            {
                if (!File.Exists(cookieFile)) throw new FileNotFoundException("Cookies 文件不存在。");
                args.AddRange(new[] { "--cookies", cookieFile });
            }
            if (!String.IsNullOrWhiteSpace(o.Agent)) args.AddRange(new[] { "--user-agent", o.Agent.Trim() });
            if (!String.IsNullOrWhiteSpace(o.Referer)) args.AddRange(new[] { "--referer", o.Referer.Trim() });
            if (!String.IsNullOrWhiteSpace(o.Rate)) args.AddRange(new[] { "--limit-rate", o.Rate.Trim() });
            if (!String.IsNullOrEmpty(password)) args.Add("--video-password=" + password);
            args.AddRange(new[] { "--no-js-runtimes", "--js-runtimes", "quickjs:" + JavaScriptRuntime() });
            return args;
        }

        internal static string CookieFileFor(Options options, string source)
        {
            if (CookieSessions.IsBrowser(options.CookieBrowser)) return null;
            if (options.CookieBrowser == CookieSessions.InApp) return CookieSessions.SavedPath(source);
            Uri url;
            if (String.IsNullOrEmpty(options.CookieBrowser) && String.IsNullOrWhiteSpace(options.Cookies) &&
                Uri.TryCreate(source, UriKind.Absolute, out url) && CookieSessions.Site(url) == "xinpianchang.com")
                return CookieSessions.SavedPath(source);
            return options.Cookies;
        }

        internal static string NormalizeBilibiliSpace(string source)
        {
            Uri uri;
            if (!Uri.TryCreate(source, UriKind.Absolute, out uri) ||
                !uri.Host.Equals("space.bilibili.com", StringComparison.OrdinalIgnoreCase)) return source;
            var match = Regex.Match(uri.AbsolutePath, @"^/([0-9]+)(?:/(?:upload|upload/video|video|dynamic|home|index))?/?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? "https://space.bilibili.com/" + match.Groups[1].Value + "/video" + uri.Query : source;
        }

        internal static bool IsCollectionResult(Dictionary<string, object> info)
        {
            string type = Value(info, "_type");
            return info.ContainsKey("entries") || type == "playlist" || type == "multi_video" || type == "compat_list";
        }

        internal static string ThumbnailUrl(Dictionary<string, object> info)
        {
            string direct = Value(info, "thumbnail");
            if (direct.Length > 0) return direct;
            return Rows(info, "thumbnails").Select(image => Value(image, "url"))
                .LastOrDefault(value => value.Length > 0) ?? "";
        }

        internal static string CollectionDirectoryName(Dictionary<string, object> info)
        {
            if (!IsCollectionResult(info)) return null;
            string name = SafeDirectoryName(Value(info, "title"));
            if (name.Length > 0) return name;
            string id = SafeDirectoryName(Value(info, "id"));
            return id.Length > 0 ? "合集 " + id : "合集";
        }

        internal static string SafeDirectoryName(string title)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string name = new string(title.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).TrimEnd('.', ' ');
            if (name.Length > 120)
            {
                int length = Char.IsHighSurrogate(name[119]) ? 119 : 120;
                name = name.Substring(0, length).TrimEnd('.', ' ');
            }
            string stem = name.Split('.')[0].TrimEnd();
            if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                name = "_" + name;
            return name;
        }

        internal static async Task<Dictionary<string, object>> Inspect(Options o, string url, bool list, CancellationToken token,
            string password = null, bool firstEntryOnly = false, Action<string> diagnostics = null)
        {
            var args = WebArgs(o, url, password);
            args.AddRange(new[] { "--skip-download", "-J" });
            if (list) args.AddRange(new[] { "--flat-playlist", "--yes-playlist" });
            else args.Add("--no-playlist");
            if (firstEntryOnly) args.AddRange(new[] { "--playlist-items", "1" });
            string target = NormalizeBilibiliSpace(url);
            if (list && target.StartsWith("https://space.bilibili.com/", StringComparison.OrdinalIgnoreCase))
                args.AddRange(new[] { "--sleep-requests", "0.8" });
            args.AddRange(new[] { "--", target });
            string result = await Capture(Downloader(), args, token, diagnostics);
            return Json().Deserialize<Dictionary<string, object>>(result);
        }

        internal static async Task<string> Capture(string tool, IEnumerable<string> args, CancellationToken token,
            Action<string> diagnostics = null)
        {
            var output = new StringBuilder();
            var errors = new List<string>();
            int code = await Run(tool, args, token, line => output.AppendLine(line), line =>
            {
                KeepError(errors, line);
                if (diagnostics != null) diagnostics(line);
            });
            if (code != 0) throw Error(code, errors);
            return output.ToString();
        }

        private static void KeepError(List<string> errors, string line)
        {
            if (errors.Count == 10) errors.RemoveAt(0);
            if (!String.IsNullOrWhiteSpace(line)) errors.Add(line);
        }

        private static Exception Error(int code, List<string> errors)
        {
            string detail = errors.Count > 0 ? String.Join(Environment.NewLine, errors) : "组件退出代码 " + code;
            if (detail.IndexOf("Could not copy", StringComparison.OrdinalIgnoreCase) >= 0 &&
                detail.IndexOf("cookie database", StringComparison.OrdinalIgnoreCase) >= 0)
                detail = "浏览器正在使用 Cookies 数据库。可以在「登录选项」使用应用内登录，或在浏览器完全退出后重试。";
            else if (detail.IndexOf("DPAPI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                detail.IndexOf("App-Bound Encryption", StringComparison.OrdinalIgnoreCase) >= 0 ||
                detail.IndexOf("could not find", StringComparison.OrdinalIgnoreCase) >= 0 &&
                detail.IndexOf("cookies database", StringComparison.OrdinalIgnoreCase) >= 0)
                detail = "无法读取所选浏览器的 Cookies。请在「登录选项」使用应用内登录，或选择 Cookies 文件。";
            else if (Regex.IsMatch(detail, @"DRM (?:protected|protection)", RegexOptions.IgnoreCase))
                return new MediaAccessException("此视频的媒体流受 DRM 保护，TubeGet 无法下载。可以试试该网站公开提供的预告或下载链接。");
            else if (detail.IndexOf("Cookies (not necessarily logged in) are needed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                detail.IndexOf("only available for registered users", StringComparison.OrdinalIgnoreCase) >= 0)
                return new MediaAccessException("此链接需要站点 Cookies。请在浏览器中确认视频能播放，再通过「登录选项」读取 Cookies。");
            else if (Regex.IsMatch(detail, @"\[vimeo(?:\+[^\]]+)?\]", RegexOptions.IgnoreCase))
            {
                if (detail.IndexOf("HTTP Error 401", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new MediaAccessException("Vimeo 播放器返回 401。请先在浏览器确认能播放，再从「登录选项」读取该浏览器的 Cookies。");
                if (detail.IndexOf("client only works when logged-in", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new MediaAccessException("Vimeo 解析需要登录。请在「登录选项」登录 Vimeo 或选择已登录的浏览器，然后重新解析。");
            }
            return new InvalidOperationException(detail);
        }

        internal static string Value(Dictionary<string, object> data, string name)
        {
            object value;
            return data.TryGetValue(name, out value) && value != null ? Convert.ToString(value, Invariant) : "";
        }

        internal static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> data, string name)
        {
            object value;
            if (!data.TryGetValue(name, out value)) return Enumerable.Empty<Dictionary<string, object>>();
            var array = value as System.Collections.IEnumerable;
            return array == null ? Enumerable.Empty<Dictionary<string, object>>() : array.OfType<Dictionary<string, object>>();
        }

        private static string VideoFormat(Options o, string exact)
        {
            if (!String.IsNullOrEmpty(exact)) return exact;
            int height;
            if (!Int32.TryParse(o.Quality, out height)) return "bv*+ba/b/bv";
            string cap = "[height<=" + height + "]";
            return "bv*" + cap + "+ba/b" + cap + "/b/bv" + cap + "/bv";
        }

        internal static async Task<string[]> Download(Options o, MediaTask job, CancellationToken token,
            Action<double, double, string> progress, Action<string> title, string password = null, Action<string> diagnostics = null)
        {
            string folder = job.Folder;
            if (job.Playlist)
            {
                if (job.PlaylistDirectoryName == null)
                {
                    var metadata = await Inspect(o, job.Source, true, token, password, firstEntryOnly: true, diagnostics: diagnostics);
                    job.PlaylistDirectoryName = CollectionDirectoryName(metadata) ?? "";
                    if (job.PlaylistDirectoryName.Length > 0) job.CollectionCoverUrl = ThumbnailUrl(metadata);
                }
                string name = SafeDirectoryName(job.PlaylistDirectoryName);
                if (name.Length > 0) folder = Path.Combine(folder, name);
            }
            Directory.CreateDirectory(folder);
            string cache = Path.Combine(Data, "partial", job.Id);
            Directory.CreateDirectory(cache);
            string manifest = Path.Combine(cache, "paths.jsonl");
            bool sidecars = job.Kind == "封面" || job.Kind == "字幕" || job.Kind == "字幕与封面";
            string rendition = !sidecars && (!String.IsNullOrEmpty(job.SelectedFormat) || job.DownloadOptions != null)
                ? " (%(format_id)s)" : "";
            string template = job.Prefix + "%(title).150B" + rendition + " [%(extractor)s-%(id)s].%(ext)s";
            if (job.Playlist) template = "%(playlist_index)03d " + template;
            var args = WebArgs(o, job.Source, password);
            args.AddRange(new[] { "--ffmpeg-location", FFmpeg(), "--continue", "--no-overwrites",
                "--newline", "--progress", "--progress-delta", "0.4",
                "--progress-template", "download:TG|%(progress._percent_str)s|%(progress.speed)s|%(progress._speed_str)s|%(progress._eta_str)s",
                "--progress-template", "postprocess:TP|%(progress.status)s", "--no-simulate",
                "--print", "before_dl:TT|%(title)s",
                "--print-to-file", "after_move:%(filepath)j", manifest.Replace("%", "%%"),
                "-P", "home:" + folder, "-P", "temp:" + cache, "-o", template,
                job.Playlist ? "--yes-playlist" : "--no-playlist" });

            if (job.Kind == "音频")
                args.AddRange(new[] { "-f", job.SelectedFormat ?? "ba/b", "-x", "--audio-format", o.AudioFormat });
            else if (job.Kind == "视频")
            {
                args.AddRange(new[] { "-f", VideoFormat(o, job.SelectedFormat) });
                if (o.Container != "默认") args.AddRange(new[] {
                    "--merge-output-format", o.Container, "--remux-video", o.Container
                });
            }
            else args.Add("--skip-download");

            if (job.Kind == "封面" || job.Kind == "字幕与封面" || (job.Kind == "视频" && o.Cover)) args.Add("--write-thumbnail");
            if (job.Kind == "字幕" || job.Kind == "字幕与封面" || (job.Kind == "视频" && o.Subtitles))
            {
                args.Add("--write-subs");
                if (o.AutoCaptions) args.Add("--write-auto-subs");
                args.AddRange(new[] { "--sub-langs", String.IsNullOrWhiteSpace(o.Languages) ? "all" : o.Languages });
                if (o.SubtitleFormat != "原始") args.AddRange(new[] { "--convert-subs", o.SubtitleFormat });
                if (job.Kind == "视频" && o.EmbedSubs) args.Add("--embed-subs");
            }
            if (o.Metadata && (job.Kind == "视频" || job.Kind == "音频")) args.Add("--embed-metadata");
            if (o.Aria)
            {
                string aria = Aria2();
                args.AddRange(new[] { "--downloader", aria, "--downloader", "dash,m3u8:native" });
                if (!String.IsNullOrWhiteSpace(o.Rate)) args.AddRange(new[] { "--downloader-args", "aria2c:--max-download-limit=" + o.Rate.Trim() });
            }
            if (sidecars)
                args.AddRange(new[] { "--print-to-file", "before_dl:%(id)j", Path.Combine(cache, "ids.jsonl").Replace("%", "%%") });
            args.AddRange(new[] { "--", NormalizeBilibiliSpace(job.Source) });

            var errors = new List<string>();
            int exit = await Run(Downloader(), args, token, line =>
            {
                if (line.StartsWith("TT|")) title(line.Substring(3));
                if (line.StartsWith("TP|")) progress(100, 0, "正在合并或处理文件…");
                if (!line.StartsWith("TG|")) return;
                string[] fields = line.Split('|');
                if (fields.Length < 5) return;
                double percent, speed;
                Double.TryParse(fields[1].Trim().TrimEnd('%'), NumberStyles.Float, Invariant, out percent);
                Double.TryParse(fields[2], NumberStyles.Float, Invariant, out speed);
                progress(percent, speed, "进度 " + fields[1].Trim() + "   ·   " + fields[3].Trim() + "   ·   剩余 " + fields[4].Trim());
            }, line =>
            {
                KeepError(errors, line);
                if (diagnostics != null) diagnostics(line);
            });
            if (exit != 0) throw Error(exit, errors);
            var files = new List<string>();
            if (File.Exists(manifest))
                foreach (string line in File.ReadAllLines(manifest, Encoding.UTF8))
                {
                    try { string path = Json().Deserialize<string>(line); if (File.Exists(path)) files.Add(path); }
                    catch (ArgumentException) { }
                }
            if (sidecars)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                string idFile = Path.Combine(cache, "ids.jsonl");
                if (File.Exists(idFile))
                    foreach (string line in File.ReadLines(idFile)) ids.Add(Json().Deserialize<string>(line));
                var extensions = new List<string>();
                if (job.Kind != "封面") extensions.AddRange(new[] { ".srt", ".vtt", ".ass", ".lrc", ".ttml", ".srv1", ".srv2", ".srv3", ".json3", ".scc", ".dfxp" });
                if (job.Kind != "字幕") extensions.AddRange(new[] { ".jpg", ".jpeg", ".png", ".webp", ".avif" });
                files.AddRange(Directory.EnumerateFiles(folder).Where(path =>
                {
                    if (!extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return false;
                    string name = Path.GetFileName(path);
                    int start = name.LastIndexOf('['), end = name.LastIndexOf(']');
                    if (start < 0 || end <= start) return false;
                    string label = name.Substring(start + 1, end - start - 1);
                    return ids.Any(id => !String.IsNullOrEmpty(id) && label.EndsWith("-" + id, StringComparison.Ordinal));
                }));
            }
            if (files.Count == 0) throw new InvalidOperationException("组件未生成文件。请检查链接、字幕语言或媒体权限。");
            try { Directory.Delete(cache, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static async Task<Dictionary<string, object>> Probe(string path, CancellationToken token)
        {
            string json = await Capture(ProbeTool(), new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", path }, token);
            return Json().Deserialize<Dictionary<string, object>>(json);
        }

        internal static async Task<string[]> ConvertMedia(MediaTask job, CancellationToken token, Action<double, double, string> progress)
        {
            Directory.CreateDirectory(job.Folder);
            var media = await Probe(job.Source, token);
            var format = media["format"] as Dictionary<string, object>;
            double seconds;
            Double.TryParse(Value(format, "duration"), NumberStyles.Float, Invariant, out seconds);
            bool audio = new[] { "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus" }.Contains(job.Format);
            TranscodePlan transcode = job.TranscodeOptions;
            bool customVideo = !audio && !job.CopyStreams && transcode != null;
            string name = Path.GetFileNameWithoutExtension(job.Source) + (job.Kind == "水印擦除" ? " - 水印擦除" : "");
            if (customVideo && transcode.AddDetailsToName)
                name += "_" + transcode.CodecFor(job.Format) + (transcode.Height > 0 ? "_max" + transcode.Height + "p" : "");
            string final = Path.Combine(job.Folder, name + "." + job.Format);
            string temp = Path.Combine(job.Folder, ".tubeget-" + job.Id + "." + job.Format);
            var args = new List<string> { "-hide_banner", "-v", "error", "-nostdin", "-y",
                "-progress", "pipe:1", "-i", job.Source };
            if (!String.IsNullOrEmpty(job.Subtitle) && !job.BurnSubtitle && !audio)
                args.AddRange(new[] { "-i", job.Subtitle });
            args.AddRange(audio ? new[] { "-map", "0:a:0", "-vn" } : new[] { "-map", "0:v:0", "-map", "0:a?" });
            var filters = new List<string>();
            if (customVideo && transcode.Height > 0)
                filters.Add("scale=-2:'trunc(min(ih," + transcode.Height + ")/2)*2'");
            if (!String.IsNullOrEmpty(job.Region)) filters.Add("delogo=" + job.Region);
            string subtitleCopy = null;
            if (!String.IsNullOrEmpty(job.Subtitle) && !audio)
            {
                if (job.BurnSubtitle)
                {
                    // Use a relative ASCII filename to avoid FFmpeg filter path escaping.
                    subtitleCopy = "subtitle-" + job.Id + Path.GetExtension(job.Subtitle);
                    Directory.CreateDirectory(Data);
                    File.Copy(job.Subtitle, Path.Combine(Data, subtitleCopy), true);
                    filters.Add("subtitles=" + subtitleCopy);
                }
                else args.AddRange(new[] { "-map", "1:0", "-c:s", job.Format == "mp4" || job.Format == "mov" ? "mov_text" : "srt" });
            }
            if (filters.Count > 0) args.AddRange(new[] { "-vf", String.Join(",", filters) });
            if (job.CopyStreams && filters.Count == 0)
            {
                args.AddRange(audio ? new[] { "-c:a", "copy" } : new[] { "-c:v", "copy", "-c:a", "copy" });
            }
            else if (customVideo) transcode.AddEncoder(args, job.Format);
            else if (!audio)
            {
                args.AddRange(job.Format == "webm"
                    ? new[] { "-c:v", "libvpx-vp9", "-crf", "31", "-b:v", "0", "-c:a", "libopus" }
                    : new[] { "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac" });
            }
            else if (job.Format == "mp3") args.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", "2" });
            else if (job.Format == "opus") args.AddRange(new[] { "-c:a", "libopus", "-b:a", "160k" });
            if (job.Format == "mp4" || job.Format == "mov") args.AddRange(new[] { "-movflags", "+faststart" });
            args.Add(temp);
            var errors = new List<string>();
            try
            {
                int exit = await Run(FFmpeg(), args, token, line =>
                {
                    long us;
                    if (line.StartsWith("out_time_us=") && Int64.TryParse(line.Substring(12), out us))
                    {
                        double done = Math.Max(0, us / 1000000.0);
                        double percent = seconds > 0 ? Math.Min(99, done / seconds * 100) : 0;
                        progress(percent, 0, String.Format(Invariant, "已处理 {0:0.0} 秒   ·   {1:0.0}%", done, percent));
                    }
                }, line => KeepError(errors, line));
                if (exit != 0) throw Error(exit, errors);
                for (int suffix = 2; ; suffix++)
                {
                    try { File.Move(temp, final); break; }
                    catch (IOException) { if (!File.Exists(final)) throw; }
                    final = Path.Combine(job.Folder, name + " (" + suffix + ")." + job.Format);
                }
                return new[] { final };
            }
            finally
            {
                TryDelete(temp);
                if (subtitleCopy != null) TryDelete(Path.Combine(Data, subtitleCopy));
            }
        }

        internal static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
