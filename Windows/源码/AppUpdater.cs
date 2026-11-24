using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace TubeGet
{
    internal sealed class AppUpdater
    {
        private const string Repository = "Jugui-Lin/TubeGet";
        private const long MaxSize = 400L * 1024 * 1024;
        private readonly string tag;
        private readonly ComponentUpdater.Asset asset;
        internal Version Version { get; private set; }

        private AppUpdater(string tag, Version version, ComponentUpdater.Asset asset)
        {
            this.tag = tag;
            this.asset = asset;
            Version = version;
        }

        internal static AppUpdater Check(IWebProxy proxy, Version current)
        {
            return Select(ComponentUpdater.FetchRelease(Repository, proxy), current);
        }

        internal static AppUpdater Select(ComponentUpdater.Release release, Version current)
        {
            Version version;
            string tag = release == null ? "" : release.tag_name ?? "";
            if (!Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$") || !Version.TryParse(tag.Substring(1), out version))
                throw new InvalidDataException("无法识别 TubeGet 的发布版本。");
            if (version <= current) return null;
            var asset = (release.assets ?? new ComponentUpdater.Asset[0]).FirstOrDefault(file => file != null && file.name == "TubeGet.exe");
            if (asset == null || asset.size <= 0 || asset.size > MaxSize ||
                !Regex.IsMatch(asset.digest ?? "", @"^sha256:[0-9a-fA-F]{64}$"))
                throw new InvalidDataException("此版本缺少可验证的 TubeGet.exe 更新文件。");
            return new AppUpdater(tag, version, asset);
        }

        internal string Download(IWebProxy proxy)
        {
            string directory = Path.Combine(Engine.Data, "app-updates");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "TubeGet-" + Version + "-" + asset.digest.Substring(7, 12) + ".exe");
            if (File.Exists(file))
            {
                try { Verify(file); return file; }
                catch (InvalidDataException) { File.Delete(file); }
            }

            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var uri = new Uri("https://github.com/" + Repository + "/releases/download/" + tag + "/TubeGet.exe");
                using (var response = ComponentUpdater.Request(uri, proxy, false))
                using (var input = response.GetResponseStream())
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    ComponentUpdater.CopyAsset(input, output, asset.size);
                Verify(temporary);
                File.Move(temporary, file);
                return file;
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal void Verify(string path)
        {
            if (new FileInfo(path).Length != asset.size) throw new InvalidDataException("更新文件大小不符。");
            ComponentUpdater.VerifyDigest(path, asset.digest);
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.ProductName != "TubeGet" || info.ProductVersion != Version.ToString())
                throw new InvalidDataException("更新文件的程序版本不符。");
        }
    }
}
