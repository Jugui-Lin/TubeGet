using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private readonly Dictionary<string, string> componentUpdateStatus = new Dictionary<string, string>();
        private bool componentUpdateRunning;
        private event Action ComponentUpdateChanged;

        private CheckBox ConfigureComponentUpdates(PreferenceCard card, Func<string> proxy)
        {
            var automatic = card.Check("启动时自动更新组件", options.AutoUpdateComponents);
            card.Row(ComponentUpdateLabel("从 GitHub 获取稳定版，更新完成后供新任务使用。", Material.Muted), 33);

            var labels = new Dictionary<string, Label>();
            foreach (var component in ComponentUpdater.Components)
            {
                var row = new Panel { BackColor = Material.Card };
                var name = ComponentUpdateLabel(component.Name, Material.Ink);
                name.Dock = DockStyle.Left;
                name.Width = 174;
                var state = ComponentUpdateLabel("", Material.Muted);
                state.Dock = DockStyle.Fill;
                labels.Add(component.Key, state);
                row.Controls.Add(state);
                row.Controls.Add(name);
                card.Row(row, 37);
            }

            var actions = new Panel { Padding = new Padding(0, 10, 0, 0), BackColor = Material.Card };
            var check = ButtonOf("检查并更新", 138, () => StartComponentUpdate(proxy()), null, "download", ButtonStyle.Filled);
            check.Dock = DockStyle.Left;
            actions.Controls.Add(check);
            card.Row(actions, 46);

            Action refresh = () =>
            {
                check.Enabled = !componentUpdateRunning;
                check.Text = componentUpdateRunning ? "正在更新…" : "检查并更新";
                foreach (var component in ComponentUpdater.Components)
                {
                    string state;
                    if (!componentUpdateStatus.TryGetValue(component.Key, out state))
                        state = Engine.ComponentUpdates.InstalledVersion(component) + " · 当前版本";
                    labels[component.Key].Text = state;
                }
            };
            ComponentUpdateChanged += refresh;
            card.View.Disposed += (sender, args) => ComponentUpdateChanged -= refresh;
            refresh();
            return automatic;
        }

        private static Label ComponentUpdateLabel(string text, Color color)
        {
            return new Label { Text = text, ForeColor = color, BackColor = Material.Card,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        }

        private void StartComponentUpdate(string proxy)
        {
            if (componentUpdateRunning) return;
            componentUpdateRunning = true;
            componentUpdateStatus.Clear();
            NotifyComponentUpdate();
            Task.Run(() =>
            {
                try
                {
                    Engine.ComponentUpdates.Update(proxy, (component, message) => Post(() =>
                    {
                        componentUpdateStatus[component.Key] = message;
                        NotifyComponentUpdate();
                    }));
                }
                finally
                {
                    Post(() =>
                    {
                        componentUpdateRunning = false;
                        NotifyComponentUpdate();
                    });
                }
            });
        }

        private void NotifyComponentUpdate()
        {
            var handler = ComponentUpdateChanged;
            if (handler != null) handler();
        }
    }
}
