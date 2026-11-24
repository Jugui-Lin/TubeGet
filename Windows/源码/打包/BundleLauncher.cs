using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TubeGet
{
    internal static class BundleLauncher
    {
        private const string ResourcePrefix = "TubeGet.Bundle.";
        private static readonly string[] RequiredFiles = {
            "TubeGet.exe", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll",
            @"components\yt-dlp\yt-dlp.exe", @"components\ffmpeg\ffmpeg.exe", @"components\ffmpeg\ffprobe.exe",
            @"components\aria2\aria2c.exe", @"components\quickjs\qjs.exe"
        };

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var assembly = typeof(BundleLauncher).Assembly;
                if (args.Length == 3 && args[0] == AppUpdateInstall.InstallCommand)
                {
                    AppUpdateInstall.Apply(assembly.Location, args[1], args[2]);
                    return 0;
                }
                if (args.Length == 0 && WindowActivation.Request()) return 0;

                string resource = assembly.GetManifestResourceNames().Single(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal));
                string releaseId = resource.Substring(ResourcePrefix.Length);
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TubeGetDesktop", "packages");
                string folder = Path.Combine(root, releaseId);
                var start = new ProcessStartInfo(Path.Combine(folder, "TubeGet.exe")) {
                    WorkingDirectory = folder,
                    Arguments = String.Join(" ", args.Select(QuoteArgument)),
                    UseShellExecute = false
                };
                start.EnvironmentVariables[AppUpdateInstall.LauncherVariable] = assembly.Location;
                bool testing = args.Length > 0 && (args[0] == "--self-test" || args[0] == "--ui-smoke");
                using (var app = StartPackage(assembly, resource, root, folder, releaseId, start, testing))
                {
                    if (!testing) return 0;
                    app.WaitForExit();
                    return app.ExitCode;
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("无法打开 TubeGet。" + Environment.NewLine + error.Message,
                    "TubeGet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static Process StartPackage(Assembly assembly, string resource, string root, string folder,
            string releaseId, ProcessStartInfo start, bool testing)
        {
            using (var gate = new Mutex(false, @"Local\TubeGet.Bundle.Cache"))
            {
                try
                {
                    if (!gate.WaitOne(TimeSpan.FromSeconds(60)))
                        throw new TimeoutException("另一个 TubeGet 窗口正在准备组件，请稍后重试。");
                }
                catch (AbandonedMutexException) { }
                try
                {
                    EnsureExtracted(assembly, resource, root, folder, releaseId);
                    var app = Process.Start(start);
                    if (app == null) throw new InvalidOperationException("无法启动 TubeGet。");
                    if (!testing) PrunePackages(root, folder);
                    return app;
                }
                finally { gate.ReleaseMutex(); }
            }
        }

        internal static void PrunePackages(string root, string current)
        {
            try
            {
                var cache = new DirectoryInfo(root);
                if ((cache.Attributes & FileAttributes.ReparsePoint) != 0) return;
                var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(current) };
                string prefix = cache.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string program in RequiredFiles.Where(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileNameWithoutExtension))
                {
                    foreach (var process in Process.GetProcessesByName(program))
                    using (process)
                    {
                        string executable;
                        try { executable = process.MainModule.FileName; }
                        catch (InvalidOperationException) { continue; }
                        catch (Win32Exception) { return; }
                        if (!executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        int end = executable.IndexOf(Path.DirectorySeparatorChar, prefix.Length);
                        if (end >= 0) inUse.Add(executable.Substring(0, end));
                    }
                }

                DateTime cutoff = DateTime.UtcNow.AddDays(-1);
                foreach (var candidate in cache.EnumerateDirectories())
                {
                    if (inUse.Contains(candidate.FullName) || (candidate.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    bool oldPackage = Regex.IsMatch(candidate.Name, @"\A[0-9a-f]{64}\z") && IsReady(candidate.FullName);
                    bool abandoned = Regex.IsMatch(candidate.Name, @"\A(?:extract|incomplete)-[0-9a-f]{32}\z") &&
                        candidate.LastWriteTimeUtc <= cutoff;
                    if (oldPackage || abandoned) TryRemoveStaging(cache.FullName, candidate.FullName);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool IsReady(string folder)
        {
            return RequiredFiles.All(name => File.Exists(Path.Combine(folder, name)));
        }

        private static void EnsureExtracted(Assembly assembly, string resource, string root, string folder, string releaseId)
        {
            if (IsReady(folder)) return;
            using (var gate = new Mutex(false, @"Local\TubeGet.Bundle." + releaseId))
            {
                try
                {
                    if (!gate.WaitOne(TimeSpan.FromSeconds(60)))
                        throw new TimeoutException("另一个 TubeGet 窗口正在准备组件，请稍后重试。");
                }
                catch (AbandonedMutexException) { }
                try
                {
                    if (IsReady(folder)) return;
                    Directory.CreateDirectory(root);
                    string pending = Path.Combine(root, "extract-" + Guid.NewGuid().ToString("N"));
                    string backup = null;
                    try
                    {
                        using (var payload = assembly.GetManifestResourceStream(resource))
                        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                            archive.ExtractToDirectory(pending);

                        if (!IsReady(pending)) throw new InvalidDataException("内置组件不完整。");
                        if (Directory.Exists(folder) && !IsReady(folder))
                        {
                            backup = Path.Combine(root, "incomplete-" + Guid.NewGuid().ToString("N"));
                            Directory.Move(folder, backup);
                        }
                        try { Directory.Move(pending, folder); }
                        catch (IOException)
                        {
                            if (!IsReady(folder))
                            {
                                if (backup != null && !Directory.Exists(folder)) Directory.Move(backup, folder);
                                throw;
                            }
                        }
                    }
                    finally
                    {
                        TryRemoveStaging(root, pending);
                        if (IsReady(folder)) TryRemoveStaging(root, backup);
                    }
                }
                finally { gate.ReleaseMutex(); }
            }
        }

        private static void TryRemoveStaging(string root, string path)
        {
            if (path == null || !String.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (Directory.Exists(path) && !ContainsReparsePoint(new DirectoryInfo(path)))
                    Directory.Delete(path, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool ContainsReparsePoint(DirectoryInfo directory)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                var subdirectory = entry as DirectoryInfo;
                if (subdirectory != null && ContainsReparsePoint(subdirectory)) return true;
            }
            return false;
        }

        private static string QuoteArgument(string argument)
        {
            var quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\') { slashes++; continue; }
                quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                quoted.Append(character);
                slashes = 0;
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
