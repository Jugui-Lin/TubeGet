using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private readonly Queue<string> runLog = new Queue<string>();
        private string doubaoDiagnosis;
        private static readonly Regex LogUrl = new Regex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

        private Action<string> RunDiagnostics(string password)
        {
            return line => RecordRunLog(line, password);
        }

        private void RecordRunLog(string message, string password = null)
        {
            if (String.IsNullOrWhiteSpace(message)) return;
            if (!String.IsNullOrEmpty(password)) message = message.Replace(password, "[视频密码]");
            message = LogUrl.Replace(message, match =>
            {
                Uri url;
                return Uri.TryCreate(match.Value, UriKind.Absolute, out url) ? url.Host + "/…" : "[链接]";
            });
            if (message.Length > 4096) message = message.Substring(0, 4096) + "…";
            lock (runLog)
            {
                if (runLog.Count >= 200) runLog.Dequeue();
                runLog.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
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

        private void ShowRunLog()
        {
            using (var dialog = Dialog("运行日志", 660, 410, new FirstPaintWindow()))
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
                var copy = ButtonOf("复制日志", 92, () => Copy(logText.Text), null, "clipboard");
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
                ask.Visible = options.AiRepair != null && options.AiRepair.Enabled && options.AiRepair.Provider == "doubao-desktop";
                Action render = () =>
                {
                    string current;
                    lock (runLog) current = String.Join(Environment.NewLine, runLog);
                    copy.Enabled = current.Length > 0;
                    ask.Enabled = doubaoDiagnosis != null;
                    if (current.Length == 0) current = "暂无运行日志";
                    if (logText.Text == current || (logText.Focused && logText.SelectionLength > 0)) return;
                    logText.Text = current;
                    logText.Select(logText.TextLength, 0);
                    logText.ScrollToCaret();
                };
                var clear = ButtonOf("清空", 72, () =>
                {
                    lock (runLog) runLog.Clear();
                    doubaoDiagnosis = null;
                    logText.Select(0, 0);
                    render();
                }, null, null, ButtonStyle.Outlined);
                footer.Controls.Add(close);
                footer.Controls.Add(copy);
                footer.Controls.Add(clear);
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
