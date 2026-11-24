using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private bool trayUsedThisSession;

        private void ConfigureTray()
        {
            var summary = new ToolStripMenuItem("TubeGet") { Enabled = false };
            var show = TrayItem("显示主窗口", "window", ShowMain);
            var hide = TrayItem("最小化到托盘", "download", HideToTray);
            var start = TrayItem("开始全部下载", "play", () => ResumeJobs(jobs.Where(job => !job.Local).ToArray()));
            var pause = TrayItem("暂停全部下载", "pause", () => PauseJobs(jobs.Where(job => !job.Local).ToArray()));
            var exit = TrayItem("退出 TubeGet", "exit", RequestExit);
            var folder = TrayItem("打开下载目录", "folder", () => OpenFolder(options.Downloads));
            var menu = new ContextMenuStrip {
                BackColor = Material.Card, ForeColor = Material.Ink, Font = UiFont(10),
                ShowImageMargin = false, Padding = new Padding(Ui(6)),
                MinimumSize = new Size(Ui(224), 0),
                Renderer = new MaterialPopupRenderer()
            };
            summary.AutoSize = false;
            summary.Size = new Size(Ui(212), Ui(38));
            menu.Items.AddRange(new ToolStripItem[] { summary, new ToolStripSeparator(), show, hide,
                new ToolStripSeparator(), start, pause, folder, new ToolStripSeparator(), exit });
            menu.Opening += (s, e) =>
            {
                int running = TrayDownloadCount();
                summary.Text = TrayStatus(running);
                summary.Tag = running > 0 ? "active" : "idle";
                hide.Visible = Visible;
                start.Enabled = !quitting && jobs.Any(job => !job.Local && job.State != "已完成" && !active.ContainsKey(job));
                pause.Enabled = !quitting && jobs.Any(job => !job.Local && (job.State == "等待中" || active.ContainsKey(job)));
                exit.Enabled = !quitting && !confirmingExit;
            };
            tray.ContextMenuStrip = menu;
            tray.Icon = trayIcon;
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
            tray.BalloonTipClicked += (s, e) => ShowMain();
            UpdateTray();
        }

        private static ToolStripMenuItem TrayItem(string title, string glyph, Action action)
        {
            var item = Item(title, action);
            item.Tag = glyph;
            item.AutoSize = false;
            item.Size = new Size(Ui(212), Ui(36));
            return item;
        }

        private void HideToTray()
        {
            trayUsedThisSession = true;
            UpdateTray();
            Hide();
        }

        private void UpdateTray()
        {
            bool visible = options.Tray || options.Notify || trayUsedThisSession;
            if (tray.Visible != visible) tray.Visible = visible;
            if (!visible) return;
            string tip = TrayStatus(TrayDownloadCount());
            if (tray.Text != tip) tray.Text = tip;
        }

        private int TrayDownloadCount()
        {
            return active.Keys.Count(job => !job.Local && job.State != "已暂停");
        }

        private static string TrayStatus(int running)
        {
            return running == 0 ? "TubeGet · 暂无下载" : "TubeGet · " + running + " 项下载中";
        }

        private DialogResult ConfirmExit()
        {
            bool busy = active.Count > 0;
            using (var dialog = Dialog("退出 TubeGet", 400, 150, new FirstPaintWindow()))
            {
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                if (!Visible || WindowState == FormWindowState.Minimized)
                {
                    var area = Screen.FromControl(this).WorkingArea;
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Load += (s, e) => dialog.Location = new Point(
                        area.Left + (area.Width - dialog.Width) / 2,
                        area.Top + (area.Height - dialog.Height) / 2);
                }

                var prompt = new Label {
                    Text = busy ? "退出前会暂停正在运行的任务。" : "确定要退出 TubeGet 吗？",
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(18, 0, 18, 0), ForeColor = Material.Ink
                };
                var footer = Footer();
                var cancel = CloseButton(dialog);
                var exit = ButtonOf(busy ? "暂停并退出" : "退出", 98, null, null, null, ButtonStyle.Filled);
                exit.DialogResult = DialogResult.OK;
                footer.Controls.Add(exit);
                footer.Controls.Add(cancel);
                if (!forceExit && Visible)
                {
                    var background = ButtonOf("最小化到托盘", 111, null, null, null, ButtonStyle.Outlined);
                    background.DialogResult = DialogResult.Ignore;
                    footer.Controls.Add(background);
                }
                dialog.AcceptButton = cancel;
                dialog.Controls.Add(prompt);
                dialog.Controls.Add(footer);
                dialog.ActiveControl = cancel;
                return ShowAppDialog(dialog, this);
            }
        }
    }
}
