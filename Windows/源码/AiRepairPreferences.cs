using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private Func<AiRepairSettings> ConfigureAiRepair(PreferenceCard card, Func<string> proxy)
        {
            var saved = options.AiRepair ?? new AiRepairSettings();
            var ids = new[] { "doubao", "openai", "codex", "ollama", "program", "doubao-desktop" };
            var names = new[] { "豆包（火山方舟 API）", "GPT（OpenAI API）", "Codex（本机程序）", "Ollama（本地模型）", "自定义本地程序", "豆包（桌面程序）" };
            int activeProvider = Array.IndexOf(ids, saved.Provider);
            if (activeProvider < 0) activeProvider = 1;
            var firstValues = new string[ids.Length];
            var secondValues = new string[ids.Length];
            string savedKey = saved.ReadKey();
            bool savedLocal = LocalAi.IsLocalProvider(saved.Provider);
            firstValues[activeProvider] = savedLocal ? saved.ProgramPath : saved.Model;
            secondValues[activeProvider] = saved.Provider == "program" ? saved.ProgramArguments : savedLocal ? saved.Model : savedKey;

            var enabled = card.Check("解析失败时自动使用 AI 诊断并尝试修复", saved.Enabled);
            var provider = card.Select("AI 服务", names, names[activeProvider]);
            var first = new TextBox();
            var second = new TextBox();
            var browse = ButtonOf("浏览", 68, () =>
            {
                using (var dialog = new OpenFileDialog {
                    Title = "选择本地 AI 程序", Filter = "可执行程序 (*.exe)|*.exe", CheckFileExists = true
                })
                    if (dialog.ShowDialog(card.View.FindForm()) == DialogResult.OK) first.Text = dialog.FileName;
            });
            var firstLabel = card.Add("", first, browse);
            var secondLabel = card.Add("", second);

            var details = new Label { ForeColor = Material.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            var privacy = new Label { ForeColor = Material.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            var status = new Label { ForeColor = Material.Muted, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0), AutoEllipsis = true };
            var actions = new Panel { Padding = new Padding(0, 5, 0, 3), BackColor = Material.Card };
            var test = ButtonOf("测试连接", 110, null, null, null, ButtonStyle.Tonal);
            test.Dock = DockStyle.Left;
            status.Dock = DockStyle.Fill;
            actions.Controls.Add(status);
            actions.Controls.Add(test);
            card.Row(details, 25);
            card.Row(privacy, 25);
            card.Row(actions, 44);

            string automaticLabel = enabled.Text;
            Action refreshFields = () =>
            {
                string id = ids[activeProvider];
                bool desktop = id == "doubao-desktop";
                bool local = LocalAi.IsLocalProvider(id);
                enabled.Text = desktop ? "解析失败后在运行日志中提供豆包诊断入口" : automaticLabel;
                firstLabel.Parent.Visible = secondLabel.Parent.Visible = !desktop;
                firstLabel.Text = first.AccessibleName = local ? (id == "program" ? "程序路径" : "程序路径（留空自动查找）") : "模型 ID";
                secondLabel.Text = second.AccessibleName = !local ? "API Key" : id == "program" ? "启动参数（可选）" :
                    id == "codex" ? "模型 ID（可选）" : "已安装的模型名称";
                browse.Parent.Visible = local;
                first.MaxLength = local ? 2048 : 200;
                second.MaxLength = !local || id == "program" ? 2048 : 200;
                second.UseSystemPasswordChar = !local;
                first.Text = firstValues[activeProvider] ?? "";
                second.Text = secondValues[activeProvider] ?? "";
                status.Text = "";
                test.Text = desktop ? "打开豆包" : local ? "测试运行" : "测试连接";
                test.AccessibleName = test.Text;
                switch (id)
                {
                    case "codex": details.Text = "调用已登录 ChatGPT 的 Codex，使用账号额度。"; break;
                    case "ollama": details.Text = "请启动本机 Ollama，并填写已下载的模型名称。"; break;
                    case "program": details.Text = "程序需从标准输入读取提示词，并将诊断 JSON 写入标准输出。"; break;
                    case "doubao-desktop": details.Text = "失败后点「运行日志 → 问豆包」，复制诊断并打开豆包。"; break;
                    case "doubao": details.Text = "填写方舟模型 ID 或推理接入点 ID（ep-…）。"; break;
                    default: details.Text = "填写 API 账户可用的 GPT 模型 ID。"; break;
                }
                privacy.Text = desktop ? "请在豆包里粘贴并发送诊断。无需 API Key。" : local ? "只传入站点类型和错误分类等诊断信息。" :
                    "仅发送站点类型和错误分类等诊断信息。模型调用使用你的 API 额度。";
            };
            refreshFields();
            provider.SelectionChanged += (s, e) =>
            {
                firstValues[activeProvider] = first.Text;
                secondValues[activeProvider] = second.Text;
                activeProvider = provider.SelectedIndex;
                refreshFields();
            };

            Func<bool, AiRepairSettings> readSettings = requireConfiguration =>
            {
                string id = ids[activeProvider], one = first.Text.Trim(), two = second.Text.Trim();
                var next = new AiRepairSettings { Enabled = enabled.Checked, Provider = id };
                if (id == "doubao-desktop") return next;
                if (LocalAi.IsLocalProvider(id))
                {
                    next.ProgramPath = one;
                    if (id == "program") next.ProgramArguments = two;
                    else next.Model = two;
                    if (requireConfiguration) LocalAi.Validate(next);
                }
                else
                {
                    if (requireConfiguration && (one.Length == 0 || two.Length == 0))
                        throw new ArgumentException("请在「AI 修复」填写模型 ID 和 API Key。");
                    next.Model = one;
                    next.ProtectedKey = id == saved.Provider && two == savedKey && two.Length > 0
                        ? saved.ProtectedKey : AiRepairSettings.Protect(two);
                }
                return next;
            };

            CancellationTokenSource pending = null;
            card.View.Disposed += (s, e) => { if (pending != null) pending.Cancel(); };
            test.Click += async (s, e) =>
            {
                if (pending != null) return;
                pending = new CancellationTokenSource();
                provider.Enabled = first.Enabled = second.Enabled = browse.Enabled = test.Enabled = false;
                status.Text = "正在测试…";
                try
                {
                    var next = readSettings(true);
                    if (next.Provider == "doubao-desktop")
                    {
                        OpenDoubao();
                        status.Text = "已请求打开豆包";
                    }
                    else
                    {
                        await AiRepair.Test(next, proxy(), pending.Token);
                        if (!card.View.IsDisposed) status.Text = LocalAi.IsLocalProvider(next.Provider) ? "运行成功" : "连接成功";
                    }
                }
                catch (OperationCanceledException) { }
                catch (ArgumentException ex) { if (!card.View.IsDisposed) status.Text = ex.Message; }
                catch (InvalidOperationException ex) { if (!card.View.IsDisposed) status.Text = ex.Message; }
                catch (System.Security.Cryptography.CryptographicException)
                { if (!card.View.IsDisposed) status.Text = "Windows 无法加密 API Key。"; }
                finally
                {
                    pending.Dispose();
                    pending = null;
                    if (!card.View.IsDisposed) provider.Enabled = first.Enabled = second.Enabled = browse.Enabled = test.Enabled = true;
                }
            };
            return () => readSettings(enabled.Checked);
        }
    }
}
