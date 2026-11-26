using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private readonly Queue<RunLogEntry> runLog = new Queue<RunLogEntry>();
        private sealed class RunLogEntry
        {
            internal string TaskId;
            internal string Text;
        }
        private string doubaoDiagnosis;
        private static readonly Regex LogUrl = new Regex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

        private Action<string> RunDiagnostics(string password, MediaTask task = null)
        {
            return line => RecordRunLog(line, password, task);
        }

        private void RecordRunLog(string message, string password = null, MediaTask task = null)
        {
            if (String.IsNullOrWhiteSpace(message)) return;
            if (!String.IsNullOrEmpty(password)) message = message.Replace(password, "[视频密码]");
            message = LogUrl.Replace(message, match =>
            {
                Uri url;
                return Uri.TryCreate(match.Value, UriKind.Absolute, out url) ? url.Host + "/…" : "[链接]";
            });
            message = Regex.Replace(message, @"(?im)\b(cookie|set-cookie|authorization|proxy-authorization|x-api-key):[^\r\n]*", "$1: [已隐藏]");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!String.IsNullOrEmpty(home)) message = Regex.Replace(message, Regex.Escape(home), "%USERPROFILE%", RegexOptions.IgnoreCase);
            if (message.Length > 4096) message = message.Substring(0, 4096) + "…";
            lock (runLog)
            {
                if (runLog.Count >= 200) runLog.Dequeue();
                string source = "";
                Uri uri;
                if (task != null && !task.Local && Uri.TryCreate(task.Source, UriKind.Absolute, out uri)) source = uri.Host + " · ";
                runLog.Enqueue(new RunLogEntry {
                    TaskId = task == null ? null : task.Id,
                    Text = DateTime.Now.ToString("HH:mm:ss") + "  " + source + message
                });
            }
        }

        private void RecordAnalysisFailure(Exception error, string source, string password = null)
        {
            RecordRunLog("解析失败 · " + error.Message, password);
            string advice = AnalysisAdvice(error, source);
            RecordRunLog("处理建议 · " + advice, password);
            Uri url;
            string host = Uri.TryCreate(source, UriKind.Absolute, out url) ? CookieSessions.Site(url) : null;
            doubaoDiagnosis = "我在 TubeGet 解析视频失败。请根据以下信息给出排查步骤。" + Environment.NewLine +
                "站点：" + (host ?? "未知") + Environment.NewLine + "错误分类：" + AiRepair.Classify(error) +
                Environment.NewLine + "已有处理建议：" + advice;
        }

        private static string AnalysisAdvice(Exception error, string source)
        {
            // AI retry errors can wrap the original site or component error.
            while (error.InnerException != null && error.Message.StartsWith("AI ", StringComparison.Ordinal))
                error = error.InnerException;

            string detail = error.Message;
            if (Regex.IsMatch(detail, @"\bDRM\b", RegexOptions.IgnoreCase))
                return "此媒体受 DRM 保护。请使用平台提供的官方下载入口，或选择可公开下载的版本。";
            if (Regex.IsMatch(detail, @"视频密码|video.password|password.protected|wrong password", RegexOptions.IgnoreCase))
                return "在「登录选项」填写或更正视频密码，再点「重新解析」。";
            if (detail.Contains("Cookies 文件不存在"))
                return "在「登录选项」重新选择有效的 Cookies 文件，再点「重新解析」。";
            if (detail.Contains("正在使用 Cookies 数据库"))
                return "完全退出浏览器后重新读取 Cookies，或在「登录选项」改用应用内登录。";
            if (detail.Contains("无法读取所选浏览器的 Cookies"))
                return "在「登录选项」改用应用内登录，或重新选择导出的 Cookies 文件。";
            if (Regex.IsMatch(detail, @"\b429\b|too many requests|限流|请求过于频繁|限制了该空间", RegexOptions.IgnoreCase))
                return "站点暂时限制请求。等几分钟再试，批量链接可以先用一条作品链接验证。";
            if (Regex.IsMatch(detail, @"未找到可用的字幕或封面"))
                return "先确认作品提供字幕或封面。只下载视频时，将模式切回「自动识别」再解析。";
            if (Regex.IsMatch(detail, @"\b401\b|login.required|log in|登录|cookies?", RegexOptions.IgnoreCase))
                return "先在浏览器登录并确认作品能播放，再在「登录选项」使用应用内登录或读取该浏览器的 Cookies，然后重新解析。";

            Uri url;
            if (Uri.TryCreate(source, UriKind.Absolute, out url) && CookieSessions.Site(url) == "xinpianchang.com" &&
                Regex.IsMatch(detail, @"HTTP Error (403|444)\b", RegexOptions.IgnoreCase))
                return "新片场页面请求被拒绝。请确认作品能播放，并核对 TubeGet 的代理设置是否与浏览器一致，再试一次。";
            if (Uri.TryCreate(source, UriKind.Absolute, out url) && CookieSessions.Site(url) == "douyin.com" &&
                url.AbsolutePath.TrimEnd('/') == "/jingxuan" && String.IsNullOrEmpty(System.Web.HttpUtility.ParseQueryString(url.Query)["modal_id"]))
                return "这条是抖音精选入口。打开要下载的作品，使用「分享 → 复制链接」，粘贴具体作品链接后再解析。";

            switch (AiRepair.Classify(error))
            {
                case "link":
                    return "在浏览器打开具体作品、合集或频道页，从地址栏或分享菜单复制完整链接，再点「重新解析」。";
                case "network":
                    return "检查浏览器能否访问该站点，并核对「设置 → 网络」中的代理配置，连接恢复后重新解析。";
                case "access":
                    return "先在浏览器确认作品仍可播放。如果是私密、会员或地区受限内容，请使用有播放权限的账号或可公开访问的作品。";
                case "extractor":
                    return "先确认作品能在浏览器播放，再到「设置 → 组件」更新组件。更新后用具体作品链接重新解析。";
                default:
                    return "先在浏览器确认作品能播放，再用完整的作品链接重新解析。如果仍失败，可复制这段日志用于排查。";
            }
        }

        internal static string TaskFailureAdvice(MediaTask task, Exception error)
        {
            string detail = error.Message;
            if (error is UnauthorizedAccessException || Regex.IsMatch(detail, "access is denied|permission denied|拒绝访问", RegexOptions.IgnoreCase))
                return "检查输出目录的写入权限，或选择另一个目录后重试。";
            if (Regex.IsMatch(detail, "no space left|disk full|not enough space|磁盘空间", RegexOptions.IgnoreCase))
                return "检查目标磁盘的剩余空间，腾出空间后重试。";
            if (task.Local)
            {
                if (!System.IO.File.Exists(task.Source)) return "请重新导入磁盘上仍存在的原文件。";
                return task.CopyStreams ? "检查目标容器是否支持原始编码，或关闭「无损转换」再试。" :
                    "检查源文件能否正常播放，并核对「转码参数」里的输出格式和编码。";
            }
            if (Regex.IsMatch(detail, @"\b403\b"))
                return "先确认当前账号能在浏览器播放所选画质，再核对 TubeGet 的登录来源和代理。确认后用原作品链接重新解析。";
            if (Regex.IsMatch(detail, "merg|ffmpeg|ffprobe", RegexOptions.IgnoreCase))
                return "到「设置 → 组件」检查 FFmpeg 和 FFprobe，再重试任务。";
            return AnalysisAdvice(error, task.Source);
        }

        private void ShowRunLog() { ShowTaskRunLog(null); }

        private void ShowTaskRunLog(MediaTask task)
        {
            using (var dialog = Dialog(task == null ? "运行日志" : "任务日志", 660, 410, new FirstPaintWindow()))
            using (var refresh = new Timer { Interval = 350 })
            {
                var logText = new TextBox {
                    Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
                    ScrollBars = ScrollBars.Vertical, BackColor = Material.Container,
                    ForeColor = Material.Ink, BorderStyle = BorderStyle.None, AccessibleName = "运行日志内容"
                };
                var logBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Material.Card };
                logBody.Controls.Add(logText);
                var footer = Footer();
                var close = ButtonOf("关闭", 78, () => dialog.Close(), null, null, ButtonStyle.Filled);
                dialog.CancelButton = close;
                string latest = "";
                var copy = ButtonOf("复制日志", 92, () => Copy(latest), null, "clipboard");
                var ask = ButtonOf("问豆包", 86, () =>
                {
                    try
                    {
                        OpenDoubao();
                        if (Copy(doubaoDiagnosis)) RecordRunLog("已复制诊断，请在豆包中粘贴并发送。");
                    }
                    catch (InvalidOperationException ex)
                    {
                        MessageBox.Show(dialog, ex.Message, "打开豆包");
                    }
                }, "复制最近一次解析诊断并打开豆包", null, ButtonStyle.Outlined);
                ask.Visible = task == null && options.AiRepair != null && options.AiRepair.Enabled && options.AiRepair.Provider == "doubao-desktop";
                bool follow = true;
                Action render = () =>
                {
                    string current;
                    lock (runLog)
                    {
                        var lines = new List<string>();
                        foreach (var entry in runLog)
                            if (task == null || entry.TaskId == task.Id) lines.Add(entry.Text);
                        current = String.Join(Environment.NewLine, lines);
                    }
                    latest = current;
                    copy.Enabled = current.Length > 0;
                    ask.Enabled = doubaoDiagnosis != null;
                    if (current.Length == 0) current = "暂无运行日志";
                    if (!follow || logText.Text == current || (logText.Focused && logText.SelectionLength > 0)) return;
                    logText.Text = current;
                    logText.Select(logText.TextLength, 0);
                    logText.ScrollToCaret();
                };
                Button followButton = null;
                followButton = ButtonOf("暂停滚动", 92, () =>
                {
                    follow = !follow;
                    followButton.Text = follow ? "暂停滚动" : "继续滚动";
                    if (follow) { logText.Select(logText.TextLength, 0); render(); }
                }, null, null, ButtonStyle.Outlined);
                var clear = ButtonOf("清空", 72, () =>
                {
                    lock (runLog) runLog.Clear();
                    doubaoDiagnosis = null;
                    follow = true;
                    followButton.Text = "暂停滚动";
                    logText.Select(0, 0);
                    render();
                }, null, null, ButtonStyle.Outlined);
                footer.Controls.Add(close);
                footer.Controls.Add(copy);
                footer.Controls.Add(followButton);
                if (task == null) footer.Controls.Add(clear);
                else clear.Dispose();
                footer.Controls.Add(ask);
                dialog.Controls.Add(logBody);
                dialog.Controls.Add(footer);
                dialog.Shown += (s, e) => close.Select();
                render();
                refresh.Tick += (s, e) => render();
                refresh.Start();
                ShowAppDialog(dialog, this);
            }
        }
    }
}
