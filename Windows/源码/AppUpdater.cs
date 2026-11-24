using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TubeGet
{
    internal sealed class AppUpdater
    {
        private const string Repository = "Jugui-Lin/TubeGet";
        private const long MaxSize = 400L * 1024 * 1024;
        private readonly string tag;
        private readonly ComponentUpdater.Asset asset;
        internal bool UsesInstaller { get { return asset.name == "TubeGet-Setup.exe"; } }
        internal Version Version { get; private set; }

        private AppUpdater(string tag, Version version, ComponentUpdater.Asset asset)
        {
            this.tag = tag;
            this.asset = asset;
            Version = version;
        }

        internal static bool IsInstalled(string executable)
        {
            using (var entry = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{5679BE52-B830-402F-BAC6-D535517586BE}_is1"))
            {
                var location = entry == null ? null : entry.GetValue("InstallLocation") as string;
                return !String.IsNullOrWhiteSpace(location) && Path.IsPathRooted(location) &&
                    String.Equals(Path.GetFullPath(Path.Combine(location, "TubeGet.exe")), executable, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static AppUpdater Check(IWebProxy proxy, Version current, bool installed = false)
        {
            return Select(ComponentUpdater.FetchRelease(Repository, proxy), current, installed);
        }

        internal static AppUpdater Select(ComponentUpdater.Release release, Version current, bool installed = false)
        {
            Version version;
            string tag = release == null ? "" : release.tag_name ?? "";
            if (!Regex.IsMatch(tag, @"\Av[0-9]+(?:\.[0-9]+){2,3}\z") || !Version.TryParse(tag.Substring(1), out version))
                throw new InvalidDataException("无法识别 TubeGet 的发布版本。");
            if (version <= current) return null;
            string filename = installed ? "TubeGet-Setup.exe" : "TubeGet.exe";
            var asset = (release.assets ?? new ComponentUpdater.Asset[0]).FirstOrDefault(file => file != null && file.name == filename);
            if (asset == null || asset.size <= 0 || asset.size > MaxSize ||
                !Regex.IsMatch(asset.digest ?? "", @"^sha256:[0-9a-fA-F]{64}$"))
                throw new InvalidDataException("此版本缺少可验证的 " + filename + " 更新文件。");
            return new AppUpdater(tag, version, asset);
        }

        internal string Download(IWebProxy proxy)
        {
            string directory = Path.Combine(Engine.Data, "app-updates");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, Path.GetFileNameWithoutExtension(asset.name) + "-" + Version +
                "-" + asset.digest.Substring(7, 12) + ".exe");
            if (File.Exists(file))
            {
                try { Verify(file); return file; }
                catch (InvalidDataException) { File.Delete(file); }
            }

            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var uri = new Uri("https://github.com/" + Repository + "/releases/download/" + tag + "/" + asset.name);
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
            if ((info.ProductName ?? "").Trim() != "TubeGet" || (info.ProductVersion ?? "").Trim() != Version.ToString())
                throw new InvalidDataException("更新文件的程序版本不符。");
        }
    }
}
