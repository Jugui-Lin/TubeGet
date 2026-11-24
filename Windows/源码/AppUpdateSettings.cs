using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private bool appUpdateRunning;

        private async void CheckAppUpdate(bool automatic)
        {
            if (appUpdateRunning)
            {
                if (!automatic) MessageBox.Show(this, "正在检查或下载更新，请稍候。", "程序更新");
                return;
            }
            appUpdateRunning = true;
            try
            {
                IWebProxy proxy = ComponentUpdater.GetProxy(options.Proxy);
                var update = await Task.Run(() => AppUpdater.Check(proxy, new Version(ReleaseInfo.Version)));
                if (IsDisposed || quitting) return;
                if (update == null)
                {
                    if (!automatic) MessageBox.Show(this, "当前已是最新版本 " + ReleaseInfo.Version + "。", "程序更新");
                    return;
                }
                if (!automatic && MessageBox.Show(this, "发现 TubeGet " + update.Version + "。下载更新？",
                    "程序更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                RecordRunLog("正在下载 TubeGet " + update.Version);
                string package = await Task.Run(() => update.Download(proxy));
                if (IsDisposed || quitting) return;
                RecordRunLog("TubeGet " + update.Version + " 已下载并通过 SHA-256 校验");
                if (active.Count > 0 || analysis != null || !Visible || confirmingExit)
                {
                    const string ready = "更新已下载。请在任务结束后从菜单选择「检查程序更新」，安装并重启。";
                    if (!automatic) MessageBox.Show(this, ready, "程序更新");
                    else if (options.Notify) tray.ShowBalloonTip(3000, "TubeGet 程序更新", ready, ToolTipIcon.Info);
                    return;
                }

                if (MessageBox.Show(this, "TubeGet " + update.Version + " 已下载。现在重启并安装更新？",
                    "程序更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                // Modal dialogs pump messages, so a task can have started since we offered to restart.
                if (active.Count > 0 || analysis != null || quitting)
                {
                    MessageBox.Show(this, "请等待当前任务结束后再安装更新。", "程序更新");
                    return;
                }
                LaunchAppUpdate(package);
            }
            catch (Exception ex)
            {
                if (IsDisposed || quitting) return;
                RecordRunLog("程序更新失败 · " + ex.Message);
                if (!automatic) MessageBox.Show(this, ex.Message, "程序更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { appUpdateRunning = false; }
        }

        private void LaunchAppUpdate(string package)
        {
            string executable = Application.ExecutablePath;
            string cached = Path.Combine(Engine.Data, "packages") + Path.DirectorySeparatorChar;
            if (executable.StartsWith(cached, StringComparison.OrdinalIgnoreCase))
            {
                executable = Environment.GetEnvironmentVariable("TUBEGET_LAUNCHER_PATH");
                if (String.IsNullOrEmpty(executable) || !Path.IsPathRooted(executable) || !File.Exists(executable))
                    throw new IOException("找不到启动用的 TubeGet.exe。请从原始启动文件打开程序后再试。");
            }
            if (dirty)
            {
                Engine.Save("tasks.json", jobs);
                dirty = false;
            }
            using (var current = Process.GetCurrentProcess())
            using (var installer = Process.Start(new ProcessStartInfo(package,
                "--install-app-update \"" + executable + "\" " + current.Id) { UseShellExecute = false }))
            {
                if (installer == null) throw new IOException("无法启动 TubeGet 更新程序。");
                quitting = true;
                Close();
            }
        }
    }
}
