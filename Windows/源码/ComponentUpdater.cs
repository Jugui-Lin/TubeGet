using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TubeGet
{
    internal sealed class ComponentUpdater
    {
        internal sealed class Component
        {
            internal readonly string Key, Name, Repository;
            internal readonly Version Bundled;
            internal readonly string[] Files;

            internal Component(string key, string name, string repository, string bundled, params string[] files)
            {
                Key = key; Name = name; Repository = repository; Bundled = new Version(bundled); Files = files;
            }

            internal string AssetPattern(Version version)
            {
                switch (Key)
                {
                    case "yt-dlp": return @"^yt-dlp\.exe$";
                    case "ffmpeg": return @"^ffmpeg-" + Regex.Escape(version.ToString()) + @"-essentials_build\.zip$";
                    case "aria2": return @"^aria2-" + Regex.Escape(version.ToString()) + @"-win-64bit-build[0-9]+\.zip$";
                    case "quickjs": return @"^qjs-windows-x86_64\.exe$";
                    default: throw new InvalidOperationException(Key);
                }
            }
        }

        internal sealed class Release
        {
            public string tag_name { get; set; }
            public Asset[] assets { get; set; }
        }

        internal sealed class Asset
        {
            public string name { get; set; }
            public long size { get; set; }
            public string digest { get; set; }
        }

        internal static readonly Component[] Components = {
            new Component("yt-dlp", "yt-dlp", "yt-dlp/yt-dlp", "2026.10.26", "yt-dlp.exe"),
            new Component("ffmpeg", "FFmpeg / FFprobe", "GyanD/codexffmpeg", "9.0.2", "ffmpeg.exe", "ffprobe.exe"),
            new Component("aria2", "aria2", "aria2/aria2", "1.37.0", "aria2c.exe"),
            new Component("quickjs", "QuickJS-NG", "quickjs-ng/quickjs", "0.18.0", "qjs.exe")
        };

        private const long MaxAssetSize = 400L * 1024 * 1024;
        private readonly string root;

        internal ComponentUpdater(string root) { this.root = root; }

        private string FindInstalled(Component component, out Version version)
        {
            version = component.Bundled;
            string best = null, directory = Path.Combine(root, component.Key);
            if (!Directory.Exists(directory)) return null;
            try
            {
                foreach (string candidate in Directory.EnumerateDirectories(directory))
                {
                    Version parsed;
                    if (!Version.TryParse(Path.GetFileName(candidate), out parsed) || parsed <= version) continue;
                    if (!component.Files.All(file => File.Exists(Path.Combine(candidate, file)))) continue;
                    best = candidate;
                    version = parsed;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return best;
        }

        internal Version InstalledVersion(Component component)
        {
            Version version;
            FindInstalled(component, out version);
            return version;
        }

        internal string Resolve(string relativePath)
        {
            var component = Components.FirstOrDefault(item => item.Key == Path.GetDirectoryName(relativePath));
            if (component == null || !component.Files.Contains(Path.GetFileName(relativePath))) return null;
            Version version;
            string directory = FindInstalled(component, out version);
            return directory == null ? null : Path.Combine(directory, Path.GetFileName(relativePath));
        }

        internal void Update(string configuredProxy, Action<Component, string> report)
        {
            var proxy = GetProxy(configuredProxy);
            foreach (var component in Components)
            {
                try
                {
                    report(component, "正在检查…");
                    Release release = Fetch(component, proxy);
                    Version current = InstalledVersion(component);
                    Version latest = ParseVersion(release);
                    if (latest <= current)
                    {
                        report(component, current + " · 已是最新");
                        continue;
                    }
                    report(component, current + " → " + latest + " · 正在下载…");
                    Install(component, release, proxy);
                    report(component, InstalledVersion(component) + " · 更新成功");
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    if (response == null) report(component, "连接 GitHub 失败（" + ex.Status + "）");
                    else
                    {
                        using (response) report(component, "GitHub 请求失败（HTTP " + (int)response.StatusCode + "）");
                    }
                }
                catch (Exception ex)
                {
                    report(component, "更新失败 · " + (ex is InvalidDataException ? ex.Message : ex.GetType().Name));
                }
            }
        }

        internal static IWebProxy GetProxy(string configuredProxy)
        {
            foreach (string candidate in new[] { configuredProxy, Environment.GetEnvironmentVariable("HTTPS_PROXY"),
                Environment.GetEnvironmentVariable("HTTP_PROXY") })
            {
                Uri uri;
                if (Uri.TryCreate(candidate, UriKind.Absolute, out uri) && uri.Scheme == "http" && uri.Host.Length > 0)
                {
                    var proxy = new WebProxy(new UriBuilder(uri) { UserName = "", Password = "" }.Uri);
                    if (uri.UserInfo.Length > 0)
                    {
                        var login = uri.UserInfo.Split(new[] { ':' }, 2);
                        proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(login[0]),
                            login.Length == 2 ? Uri.UnescapeDataString(login[1]) : "");
                    }
                    return proxy;
                }
            }
            return WebRequest.DefaultWebProxy;
        }

        internal static Release Fetch(Component component, IWebProxy proxy)
        {
            var uri = new Uri("https://api.github.com/repos/" + component.Repository + "/releases/latest");
            using (var response = Request(uri, proxy, true))
            using (var input = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                char[] buffer = new char[1024 * 1024];
                int count = input.ReadBlock(buffer, 0, buffer.Length);
                if (count == buffer.Length) throw new InvalidDataException("GitHub 发布信息过大。");
                return Engine.Json().Deserialize<Release>(new string(buffer, 0, count));
            }
        }

        private static Version ParseVersion(Release release)
        {
            string tag = release == null ? null : release.tag_name;
            var match = Regex.Match(tag ?? "", @"^(?:v|release-)?([0-9]+(?:\.[0-9]+){1,3})$");
            Version version;
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out version))
                throw new InvalidDataException("无法识别 GitHub 版本号。");
            return version;
        }

        internal void Install(Component component, Release release, IWebProxy proxy)
        {
            Version version = ParseVersion(release);
            string pattern = component.AssetPattern(version);
            var asset = (release.assets ?? new Asset[0]).Where(item => item != null && Regex.IsMatch(item.name ?? "", pattern))
                .OrderByDescending(item => AssetBuild(item.name)).FirstOrDefault();
            if (asset == null || asset.size <= 0 || asset.size > MaxAssetSize)
                throw new InvalidDataException("找不到适用的 Windows x64 发布文件。");
            string directory = Path.Combine(root, component.Key);
            string target = Path.Combine(directory, version.ToString());
            if (component.Files.All(file => File.Exists(Path.Combine(target, file)))) return;
            string staging = Path.Combine(directory, "pending-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                string payload = Path.Combine(staging, "download");
                var uri = new Uri("https://github.com/" + component.Repository + "/releases/download/" +
                    Uri.EscapeDataString(release.tag_name) + "/" + Uri.EscapeDataString(asset.name));
                using (var response = Request(uri, proxy, false))
                using (var input = response.GetResponseStream())
                using (var output = File.Create(payload))
                    CopyAsset(input, output, asset.size);

                VerifyDigest(payload, asset.digest);
                if (asset.name.EndsWith(".zip", StringComparison.Ordinal))
                    ExtractBinaries(payload, staging, component);
                else
                    File.Move(payload, Path.Combine(staging, component.Files[0]));
                foreach (string file in component.Files)
                    VerifyExecutable(Path.Combine(staging, file));
                File.Delete(payload);
                try { Directory.Move(staging, target); }
                catch (IOException)
                {
                    // Another running instance may have published this release during the download.
                    if (!component.Files.All(file => File.Exists(Path.Combine(target, file)))) throw;
                }
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static int AssetBuild(string name)
        {
            var match = Regex.Match(name, @"-build([0-9]+)\.zip$");
            int number;
            return match.Success && Int32.TryParse(match.Groups[1].Value, out number) ? number : 0;
        }

        private static HttpWebResponse Request(Uri uri, IWebProxy proxy, bool metadata)
        {
            // Legacy .NET Framework executables may default to TLS 1.0 instead of system settings.
            if (ServicePointManager.SecurityProtocol != 0)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 ||
                    !(metadata ? uri.Host == "api.github.com" : new[] { "github.com", "release-assets.githubusercontent.com",
                        "objects.githubusercontent.com", "github-releases.githubusercontent.com" }.Contains(uri.Host)))
                    throw new InvalidDataException("GitHub 返回了无效的更新地址。");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false;
                request.UserAgent = "TubeGet";
                request.Accept = metadata ? "application/vnd.github+json" : "application/octet-stream";
                request.Timeout = 20000;
                request.ReadWriteTimeout = 30000;
                request.Proxy = proxy;
                var response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode == HttpStatusCode.OK) return response;
                using (response)
                {
                    string location = response.Headers[HttpResponseHeader.Location];
                    if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400 ||
                        String.IsNullOrEmpty(location) || !Uri.TryCreate(uri, location, out uri))
                        throw new InvalidDataException("GitHub 返回了无效的更新响应。");
                }
            }
            throw new InvalidDataException("GitHub 更新地址重定向次数过多。");
        }

        private static void CopyAsset(Stream input, Stream output, long expected)
        {
            var buffer = new byte[128 * 1024];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > expected) throw new InvalidDataException("下载文件大小校验失败。");
                output.Write(buffer, 0, read);
            }
            if (total != expected) throw new InvalidDataException("下载文件不完整。");
        }

        private static void VerifyDigest(string path, string digest)
        {
            if (String.IsNullOrEmpty(digest)) return;
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
            {
                string actual = "sha256:" + BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                if (!String.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 校验失败。");
            }
        }

        private static void ExtractBinaries(string path, string staging, Component component)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                foreach (string file in component.Files)
                {
                    var matches = zip.Entries.Where(item => String.Equals(Path.GetFileName(item.FullName), file,
                        StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                    if (matches.Length != 1 || matches[0].Length <= 0 || matches[0].Length > MaxAssetSize)
                        throw new InvalidDataException("压缩包中缺少有效的 " + file + "。");
                    using (var input = matches[0].Open())
                    using (var output = File.Create(Path.Combine(staging, file)))
                        CopyAsset(input, output, matches[0].Length);
                }
                foreach (var entry in zip.Entries)
                {
                    string name = Path.GetFileName(entry.FullName);
                    if (entry.Length <= 0 || entry.Length > 1024 * 1024 ||
                        !Regex.IsMatch(name, @"^(?:license|copying|notice|readme)(?:[.-][a-z0-9]+)*$", RegexOptions.IgnoreCase)) continue;
                    string target = Path.Combine(staging, name);
                    if (File.Exists(target)) continue;
                    using (var input = entry.Open())
                    using (var output = File.Create(target))
                        CopyAsset(input, output, entry.Length);
                }
            }
        }

        private static void VerifyExecutable(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.BaseStream.Length >= 64 && reader.ReadUInt16() == 0x5A4D)
                {
                    reader.BaseStream.Position = 0x3C;
                    int offset = reader.ReadInt32();
                    if (offset >= 64 && offset <= reader.BaseStream.Length - 24)
                    {
                        reader.BaseStream.Position = offset;
                        if (reader.ReadUInt32() == 0x4550 && reader.ReadUInt16() == 0x8664) return;
                    }
                }
                throw new InvalidDataException("文件不是有效的 Windows x64 程序。");
            }
        }
    }
}
