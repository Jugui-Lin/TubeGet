using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TubeGet
{
    internal static class AppUpdateInstall
    {
        internal const string LauncherVariable = "TUBEGET_LAUNCHER_PATH";
        internal const string InstallCommand = "--install-app-update";

        internal static void Apply(string source, string target, string parentId)
        {
            int pid;
            if (!Int32.TryParse(parentId, out pid) || pid <= 0 || pid == Process.GetCurrentProcess().Id ||
                !Path.IsPathRooted(target) || !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("更新请求无效。");
            target = Path.GetFullPath(target);
            try
            {
                using (var parent = Process.GetProcessById(pid))
                    if (!parent.WaitForExit(30000)) throw new IOException("TubeGet 尚未退出，请稍后重试。");
            }
            catch (ArgumentException) { } // The old app already exited.

            var installed = FileVersionInfo.GetVersionInfo(target);
            var next = FileVersionInfo.GetVersionInfo(source);
            Version installedVersion, nextVersion;
            if (installed.ProductName != "TubeGet" || next.ProductName != "TubeGet" ||
                !Version.TryParse(installed.ProductVersion, out installedVersion) ||
                !Version.TryParse(next.ProductVersion, out nextVersion) || nextVersion <= installedVersion)
                throw new InvalidDataException("只能将现有的 TubeGet 更新到较新的版本。");

            string staging = target + "." + Guid.NewGuid().ToString("N");
            string backup = staging + ".bak";
            try
            {
                File.Copy(source, staging);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Replace(staging, target, backup); break; }
                    catch (IOException)
                    {
                        if (attempt >= 9) throw;
                        Thread.Sleep(200);
                    }
                }
                try
                {
                    using (var app = Process.Start(new ProcessStartInfo(target) {
                        UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)
                    }))
                    {
                        if (app == null || (app.WaitForExit(2000) && app.ExitCode != 0))
                            throw new IOException("新版 TubeGet 启动失败。");
                    }
                }
                catch
                {
                    File.Replace(backup, target, null);
                    throw;
                }
                TryDelete(backup);
            }
            finally { TryDelete(staging); }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
