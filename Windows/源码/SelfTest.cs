using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TubeGet
{
    internal static class SelfTest
    {
        internal static async Task<int> Run(string[] args)
        {
            string root = Path.Combine(Path.GetTempPath(), "TubeGet-test-" + Guid.NewGuid().ToString("N"));
            string report = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "TubeGet-test.log");
            var results = new List<string>();
            var o = new Options { Downloads = Path.Combine(root, "下载"), Converted = Path.Combine(root, "转换") };
            var server = new TcpListener(IPAddress.Loopback, 0);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180)))
            {
                try
                {
                    Directory.CreateDirectory(root);
                    Engine.UseTestData(Path.Combine(root, "data"));
                    if (Engine.NormalizeBilibiliSpace("https://space.bilibili.com/3985676/dynamic?spm_id_from=333") !=
                        "https://space.bilibili.com/3985676/video?spm_id_from=333" ||
                        Engine.NormalizeBilibiliSpace("https://space.bilibili.com/3985676/lists/22") != "https://space.bilibili.com/3985676/lists/22")
                        throw new Exception("B 站空间链接归一化失败。");
                    if (CookieSessions.Site(new Uri("https://v.douyin.com/example/")) != "douyin.com" ||
                        CookieSessions.Site(new Uri("https://www.iesdouyin.com/share/video/123/")) != "douyin.com" ||
                        CookieSessions.Site(new Uri("https://notdouyin.com/")) == "douyin.com")
                        throw new Exception("抖音会话站点识别失败。");
                    var legacy = Engine.Json().Deserialize<Options>("{\"Components\":\"missing\",\"Theme\":\"dark\",\"Parallel\":3}");
                    if (legacy.Theme != "dark" || legacy.Parallel != 3) throw new Exception("旧版设置读取失败。");
                    string recovery = Path.Combine(root, "recovery.json");
                    File.WriteAllText(recovery, "{");
                    File.WriteAllText(recovery + ".bak", "{\"Theme\":\"dotin-dark\",\"ReduceMotion\":true}");
                    var restored = Engine.ReadWithBackup(recovery, new Options());
                    if (MainForm.NormalizeTheme(restored.Theme) != "dark" || !restored.ReduceMotion ||
                        MainForm.NormalizeTheme("dotin-light") != "light" || MainForm.NormalizeTheme("unknown") != "system")
                        throw new Exception("损坏设置的备份恢复失败。");
                    foreach (string tag in new[] { "2026.11.25", "v2026.11.25.0", "V2026.11.25.0" })
                        if (AppUpdater.Select(new ComponentUpdater.Release { tag_name = tag }, new Version("2026.11.25")) != null)
                            throw new Exception("等效版本号被识别为更新。");
                    if (!MainForm.TaskFailureAdvice(new MediaTask { Source = "https://example.test/media" },
                        new IOException("HTTP Error 403: Forbidden")).Contains("所选画质"))
                        throw new Exception("下载 403 缺少可操作的诊断。");
                    results.Add("PASS: backup recovery, theme migration, release tag formats and HTTP 403 advice");
                    string js = await Engine.Capture(Engine.JavaScriptRuntime(), new[] { "-e", "console.log(21 * 2)" }, timeout.Token);
                    if (js.Trim() != "42") throw new Exception("QuickJS 自检失败。");
                    results.Add("PASS: bundled QuickJS");
                    string video = Path.Combine(root, "示例 文件.mp4");
                    await Engine.Capture(Engine.FFmpeg(), new[] { "-v", "error", "-y", "-f", "lavfi", "-i",
                        "testsrc2=size=320x180:rate=24", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "3",
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", video }, timeout.Token);
                    using (var cover = await MainForm.ReadCompletedCover(video, "视频"))
                    {
                        if (cover == null || cover.Width != 320 || cover.Height != 180)
                            throw new Exception("已完成卡片无法从视频生成封面。");
                    }
                    results.Add("PASS: completed-card cover generated from local video");
                    var collectionJobs = new[]
                    {
                        new MediaTask { File = Path.Combine(root, "missing.mp4"), Kind = "视频" },
                        new MediaTask { Playlist = true, Files = new[] { video }, Kind = "视频" }
                    };
                    using (var cover = await MainForm.ReadCompletedFolderCover(null, collectionJobs))
                    {
                        if (cover == null || cover.Width != 320 || cover.Height != 180)
                            throw new Exception("已完成的旧合集无法从分集生成封面。");
                    }
                    results.Add("PASS: completed collection cover generated from available playlist video");
                    byte[] content = File.ReadAllBytes(video);
                    server.Start();
                    var serve = Serve(server, content);
                    string url = "http://127.0.0.1:" + ((IPEndPoint)server.LocalEndpoint).Port + "/sample.mp4";
                    var info = await Engine.Inspect(o, url, true, timeout.Token);
                    if (info.ContainsKey("entries") || !Engine.Rows(info, "formats").Any()) throw new Exception("视频格式表为空。");
                    results.Add("PASS: HTTP media URL and format inspection");

                    Action<double, double, string> progress = (p, speed, detail) => { };
                    foreach (string kind in new[] { "视频", "音频" })
                    {
                        var job = new MediaTask { Source = url, Kind = kind, Prefix = "", Folder = o.Downloads };
                        var files = await Engine.Download(o, job, timeout.Token, progress, title => { });
                        var downloaded = await Engine.Probe(files[0], timeout.Token);
                        string required = kind == "音频" ? "audio" : "video";
                        if (!Engine.Rows(downloaded, "streams").Any(s => Engine.Value(s, "codec_type") == required))
                            throw new Exception("下载文件缺少 " + required);
                        results.Add("PASS: " + kind + "下载及输出验证");
                    }
                    string id = Engine.Value(Engine.Rows(info, "formats").First(), "format_id");
                    var exact = await Engine.Download(o, new MediaTask { Source = url, Kind = "视频", Prefix = "", Folder = o.Downloads, SelectedFormat = id },
                        timeout.Token, progress, title => { });
                    if (!Path.GetFileName(exact[0]).Contains("(" + id + ")") || !File.ReadAllBytes(exact[0]).SequenceEqual(content))
                        throw new Exception("指定格式未生成独立的媒体文件。");
                    results.Add("PASS: exact format with distinct output path");
                    string legacyPart = Path.Combine(root, "legacy-part");
                    Directory.CreateDirectory(legacyPart);
                    if (Engine.DownloadTransferCache(legacyPart, true) != legacyPart)
                        throw new Exception("完整原生续传路径被更改。");
                    File.WriteAllText(Path.Combine(legacyPart, "sample.mp4.part.aria2"), "fixture");
                    if (Engine.DownloadTransferCache(legacyPart, true) != Path.Combine(legacyPart, "native") ||
                        Engine.DownloadTransferCache(legacyPart, false) != legacyPart ||
                        Engine.ConstrainFormatDimensions("4", 3840, 2160) != "4[width=3840][height=2160]")
                        throw new Exception("新片场续传路径或画质约束不正确。");
                    results.Add("PASS: 2160p constraint and isolation of aria2 resume state");

                    var recoveryServer = new TcpListener(IPAddress.Loopback, 0);
                    recoveryServer.Start();
                    try
                    {
                        string local = "http://127.0.0.1:" + ((IPEndPoint)recoveryServer.LocalEndpoint).Port;
                        int pages = 0, rejected = 0, resumedAt = 0;
                        var recoveryServe = ServeFiles(recoveryServer,
                            new Dictionary<string, byte[]> { { "/fresh.mp4", content } }, path =>
                            {
                                if (path != "/refresh.html") return null;
                                string name = Interlocked.Increment(ref pages) == 1 ? "stale.mp4" : "fresh.mp4";
                                return Encoding.UTF8.GetBytes("<html><head><title>Refresh sample</title></head><body><video src=\"" +
                                    local + "/" + name + "\"></video></body></html>");
                            }, path =>
                            {
                                if (path != "/stale.mp4" && path != "/refused.html") return false;
                                Interlocked.Increment(ref rejected);
                                return true;
                            }, (path, offset) => { if (path == "/fresh.mp4") Interlocked.Exchange(ref resumedAt, offset); });
                        var messages = new List<string>();
                        var retryJob = new MediaTask { Source = local + "/refresh.html", Kind = "视频", Folder = o.Downloads };
                        var recovered = await Engine.Download(o, retryJob, timeout.Token, progress, title => { }, diagnostics: messages.Add);
                        if (pages != 2 || rejected != 1 || !messages.Any(line => line.Contains("仅重试一次")) ||
                            !File.ReadAllBytes(recovered.Single()).SequenceEqual(content))
                            throw new Exception("媒体 403 后未重新解析并保存完整媒体。");
                        string cache = Path.Combine(Engine.Data, "partial", retryJob.Id);
                        Directory.CreateDirectory(cache);
                        string fullOutput = recovered.Single();
                        int completedBytes = content.Length / 3;
                        File.WriteAllBytes(Path.Combine(cache, Path.GetFileName(fullOutput) + ".part"), content.Take(completedBytes).ToArray());
                        File.Delete(fullOutput);
                        var resumed = await Engine.Download(o, retryJob, timeout.Token, progress, title => { });
                        if (resumedAt != completedBytes || !File.ReadAllBytes(resumed.Single()).SequenceEqual(content))
                            throw new Exception("断点续传未使用正确的 Range，或输出文件与原数据不同。");
                        messages.Clear();
                        try
                        {
                            await Engine.Download(o, new MediaTask {
                                Source = local + "/refused.html", Kind = "视频", Folder = o.Downloads
                            }, timeout.Token, progress, title => { }, diagnostics: messages.Add);
                            throw new Exception("作品页 403 被误判为成功。");
                        }
                        catch (InvalidOperationException ex)
                        {
                            if (!ex.Message.Contains("403") || rejected != 2 || messages.Any(line => line.Contains("仅重试一次"))) throw;
                        }
                    }
                    finally { recoveryServer.Stop(); }
                    results.Add("PASS: expired media URL retries once, native resume uses Range, forbidden work page does not retry");
                    o.Aria = true;
                    var aria = await Engine.Download(o, new MediaTask { Source = url, Kind = "视频", Prefix = "aria2 ", Folder = o.Downloads },
                        timeout.Token, progress, title => { });
                    if (!File.ReadAllBytes(aria[0]).SequenceEqual(content)) throw new Exception("aria2 下载文件校验失败。");
                    results.Add("PASS: bundled aria2 via yt-dlp");
                    o.Aria = false;
                    string playlist = Path.Combine(root, "stream.m3u8");
                    await Engine.Capture(Engine.FFmpeg(), new[] { "-v", "error", "-i", video, "-c", "copy",
                        "-hls_time", "1", "-hls_list_size", "0", playlist }, timeout.Token);
                    server.Stop();
                    var hlsFiles = Directory.GetFiles(root, "stream*").ToDictionary(p => "/" + Path.GetFileName(p), File.ReadAllBytes);
                    server = new TcpListener(IPAddress.Loopback, 0);
                    server.Start();
                    serve = ServeFiles(server, hlsFiles);
                    string streamUrl = "http://127.0.0.1:" + ((IPEndPoint)server.LocalEndpoint).Port + "/stream.m3u8";
                    var stream = await Engine.Download(o, new MediaTask { Source = streamUrl, Kind = "视频", Prefix = "", Folder = o.Downloads },
                        timeout.Token, progress, title => { });
                    if (!Engine.Rows(await Engine.Probe(stream[0], timeout.Token), "streams").Any(s => Engine.Value(s, "codec_type") == "video"))
                        throw new Exception("HLS 输出无视频流。");
                    results.Add("PASS: HLS segmented download");
                    string picture = Path.Combine(root, "cover.jpg");
                    await Engine.Capture(Engine.FFmpeg(), new[] { "-v", "error", "-i", video, "-frames:v", "1", picture }, timeout.Token);
                    server.Stop();
                    server = new TcpListener(IPAddress.Loopback, 0);
                    server.Start();
                    string site = "http://127.0.0.1:" + ((IPEndPoint)server.LocalEndpoint).Port;
                    var html = "<html><head><title>Demo</title><meta property=\"og:image\" content=\"" + site +
                        "/cover.jpg\"></head><body><video src=\"" + site + "/sample.mp4\" poster=\"" + site + "/cover.jpg\">" +
                        "<track kind=\"subtitles\" srclang=\"en\" src=\"" + site + "/sample.vtt\"></video></body></html>";
                    serve = ServeFiles(server, new Dictionary<string, byte[]> {
                        { "/watch.html", Encoding.UTF8.GetBytes(html) }, { "/cover.jpg", File.ReadAllBytes(picture) }, { "/sample.mp4", content },
                        { "/sample.vtt", Encoding.UTF8.GetBytes("WEBVTT\n\n00:00:00.200 --> 00:00:02.100\nTubeGet test\n") },
                        { "/second.mp4", content },
                        { "/collection.html", Encoding.UTF8.GetBytes("<html><head><title>Two clips</title></head><body>" +
                            "<video src=\"" + site + "/sample.mp4\"></video><video src=\"" + site + "/second.mp4\"></video></body></html>") }
                    });
                    using (var cover = await MainForm.ReadCompletedFolderCover(site + "/cover.jpg", new MediaTask[0]))
                    {
                        if (cover == null || cover.Width != 320 || cover.Height != 180)
                            throw new Exception("已完成合集无法加载解析得到的封面。");
                    }
                    results.Add("PASS: completed collection cover loaded from its parsed image URL");
                    var collection = await Engine.Inspect(o, site + "/collection.html", true, timeout.Token);
                    if (Engine.Rows(collection, "entries").Count() != 2) throw new Exception("无法识别双视频列表。");
                    results.Add("PASS: automatic inspection of a page with two video entries");
                    var invalidTitle = Engine.Json().Deserialize<Dictionary<string, object>>(
                        "{\"_type\":\"playlist\",\"title\":\"CON.示例/合集\"}");
                    if (Engine.CollectionDirectoryName(collection) != "Two clips" ||
                        Engine.CollectionDirectoryName(invalidTitle) != "_CON.示例_合集" ||
                        Engine.CollectionDirectoryName(info) != null)
                        throw new Exception("合集目录名称识别失败。");
                    var playlistJob = new MediaTask {
                        Source = site + "/collection.html", Kind = "视频", Prefix = "", Folder = o.Downloads, Playlist = true
                    };
                    var playlistFiles = await Engine.Download(o, playlistJob, timeout.Token, progress, title => { });
                    if (playlistFiles.Length != 2 || playlistJob.PlaylistDirectoryName != "Two clips" ||
                        playlistFiles.Any(path => Path.GetDirectoryName(path) != Path.Combine(o.Downloads, "Two clips")))
                        throw new Exception("整份合集的文件未保存在同名子目录。");
                    results.Add("PASS: complete playlist downloaded to its title folder");
                    var covers = await Engine.Download(o, new MediaTask { Source = site + "/watch.html", Kind = "封面", Prefix = "", Folder = o.Downloads },
                        timeout.Token, progress, title => { });
                    if (!covers.Any(p => Path.GetExtension(p).Equals(".jpg", StringComparison.OrdinalIgnoreCase))) throw new Exception("封面未保存。");
                    results.Add("PASS: cover-only download");
                    o.Languages = "^en$";
                    var sidecarFiles = await Engine.Download(o, new MediaTask { Source = site + "/watch.html", Kind = "字幕与封面", Prefix = "", Folder = o.Downloads },
                        timeout.Token, progress, title => { });
                    if (!sidecarFiles.Any(p => Path.GetExtension(p) == ".srt") || !sidecarFiles.Any(p => Path.GetExtension(p) == ".jpg"))
                        throw new Exception("字幕与封面未同时保存。");
                    results.Add("PASS: combined subtitle and cover without video");
                    string subtitle = Path.Combine(root, "中文字幕.srt");
                    File.WriteAllText(subtitle, "1\r\n00:00:00,200 --> 00:00:02,100\r\nTubeGet 测试\r\n", new UTF8Encoding(false));
                    var jobs = new[] {
                        new MediaTask { Source = video, Kind = "转换", Format = "mkv", Folder = o.Converted, Subtitle = subtitle },
                        new MediaTask { Source = video, Kind = "转换", Format = "mp4", Folder = o.Converted, Subtitle = subtitle, BurnSubtitle = true },
                        new MediaTask { Source = video, Kind = "转换", Format = "webm", Folder = o.Converted },
                        new MediaTask { Source = video, Kind = "转换", Format = "mp3", Folder = o.Converted },
                        new MediaTask { Source = video, Kind = "转换", Format = "mov", Folder = o.Converted, CopyStreams = true },
                        new MediaTask { Source = video, Kind = "水印擦除", Format = "mp4", Folder = o.Converted, Region = "x=16:y=20:w=52:h=24" }
                    };
                    foreach (var job in jobs)
                    {
                        var output = await Engine.ConvertMedia(job, timeout.Token, progress);
                        var probe = await Engine.Probe(output[0], timeout.Token);
                        var streams = Engine.Rows(probe, "streams").Select(s => Engine.Value(s, "codec_type")).ToArray();
                        if (job.Format == "mp3" ? !streams.SequenceEqual(new[] { "audio" }) : !streams.Contains("video"))
                            throw new Exception("转码输出无效：" + output[0]);
                        if (job.Format == "mkv" && !streams.Contains("subtitle")) throw new Exception("找不到软字幕轨道。");
                        results.Add("PASS: " + job.Kind + " " + job.Format + (job.BurnSubtitle ? " 硬字幕" : job.Subtitle != null ? " 软字幕" : job.CopyStreams ? " 无损封装" : ""));
                    }
                    var scaled = new MediaTask { Source = video, Kind = "转换", Format = "mp4", Folder = o.Converted,
                        TranscodeOptions = new TranscodePlan { Codec = "H.264", Height = 120, BitrateMbps = 1, Device = "CPU", AddDetailsToName = true } };
                    var scaledFiles = await Engine.ConvertMedia(scaled, timeout.Token, progress);
                    var scaledInfo = await Engine.Probe(scaledFiles[0], timeout.Token);
                    var scaledVideo = Engine.Rows(scaledInfo, "streams").First(s => Engine.Value(s, "codec_type") == "video");
                    if (Engine.Value(scaledVideo, "height") != "120" || !Path.GetFileName(scaledFiles[0]).Contains("_max120p"))
                        throw new Exception("高级转码参数未应用。");
                    results.Add("PASS: transcode resolution, bitrate and output naming");
                    File.WriteAllLines(report, results, Encoding.UTF8);
                    return 0;
                }
                catch (Exception ex)
                {
                    results.Add("FAIL: " + ex);
                    File.WriteAllLines(report, results, Encoding.UTF8);
                    return 1;
                }
                finally
                {
                    server.Stop();
                    try { Directory.Delete(root, true); } catch (IOException) { }
                }
            }
        }

        private static Task Serve(TcpListener server, byte[] content)
        {
            return ServeFiles(server, new Dictionary<string, byte[]> { { "/sample.mp4", content } });
        }

        private static async Task ServeFiles(TcpListener server, Dictionary<string, byte[]> files,
            Func<string, byte[]> dynamicFile = null, Func<string, bool> forbidden = null, Action<string, int> resume = null)
        {
            while (true)
            {
                try
                {
                    var client = await server.AcceptTcpClientAsync();
                    StartFixtureResponse(client, files, dynamicFile, forbidden, resume);
                }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                catch (SocketException) { return; }
            }
        }

        private static async void StartFixtureResponse(TcpClient client, Dictionary<string, byte[]> files,
            Func<string, byte[]> dynamicFile, Func<string, bool> forbidden, Action<string, int> resume)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
                {
                    string first = await reader.ReadLineAsync();
                    if (String.IsNullOrEmpty(first)) return;
                        string header;
                        int requestedStart = 0;
                        while (!String.IsNullOrEmpty(header = await reader.ReadLineAsync()))
                            if (header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                                Int32.TryParse(header.Substring("Range: bytes=".Length).Split('-')[0], out requestedStart);
                        string[] fields = first.Split(' ');
                        byte[] content;
                        string url = fields.Length > 1 ? fields[1] : "";
                        bool denied = forbidden != null && forbidden(url);
                        if (!files.TryGetValue(url, out content) && dynamicFile != null) content = dynamicFile(url);
                        bool found = !denied && content != null;
                        if (!found) content = new byte[0];
                        string type = url.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" :
                            url.EndsWith(".ts") ? "video/mp2t" : url.EndsWith(".html") ? "text/html" :
                            url.EndsWith(".jpg") ? "image/jpeg" : "video/mp4";
                        int start = found && resume != null && requestedStart > 0 && requestedStart < content.Length ? requestedStart : 0;
                        string range = start == 0 ? "" : "\r\nContent-Range: bytes " + start + "-" + (content.Length - 1) + "/" + content.Length;
                        if (start != 0) resume(url, start);
                        string status = start != 0 ? "206 Partial Content" : found ? "200 OK" : denied ? "403 Forbidden" : "404 Not Found";
                        byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + range +
                            "\r\nContent-Type: " + type + "\r\nContent-Length: " + (content.Length - start) + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(response, 0, response.Length);
                        if (fields[0] != "HEAD") await stream.WriteAsync(content, start, content.Length - start);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
    }
}
