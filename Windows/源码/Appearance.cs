using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TubeGet
{
    internal sealed partial class MainForm
    {
        private static readonly string[] ThemeModes = { "system", "light", "dark" };
        private static readonly string[] ThemeLabels = { "跟随系统", "浅色", "深色" };

        internal static string NormalizeTheme(string mode)
        {
            switch (mode)
            {
                case "light":
                case "dotin-light": return "light";
                case "dark":
                case "dotin-dark": return "dark";
                default: return "system";
            }
        }

        private void ToggleTheme()
        {
            options.Theme = Material.Dark ? "light" : "dark";
            var button = heroActions.Controls.Cast<Control>().OfType<MaterialButton>().FirstOrDefault(x => x.Glyph == "theme");
            Point center = button == null ? new Point(ClientSize.Width / 2, ClientSize.Height / 2) :
                PointToClient(button.PointToScreen(new Point(button.Width / 2, button.Height / 2)));
            if (options.ReduceMotion) ApplyTheme();
            else ThemeTransition.Run(this, center, ApplyTheme);
            SaveOptions();
        }

        private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.VisualStyle) Post(ApplyTheme);
        }

        private void ApplyTheme()
        {
            bool dark = Material.ResolveDark(options.Theme);
            if (dark == Material.Dark) return;
            Color[] before = Material.Palette();
            Material.SetTheme(dark);
            Color[] after = Material.Palette();
            Func<Color, Color> convert = color =>
            {
                int index = Array.IndexOf(before, color);
                return index < 0 ? color : after[index];
            };
            foreach (Form form in Application.OpenForms)
            {
                if (form is ThemeTransition) continue;
                Recolor(form, convert);
                Material.UpdateChrome(form);
                form.Invalidate(true);
            }
            if (downloadOptionsPanel.Parent == null) Recolor(downloadOptionsPanel, convert);
            if (tray.ContextMenuStrip != null) Recolor(tray.ContextMenuStrip, convert);
            RefreshTasks();
        }

        private static void Recolor(Control control, Func<Color, Color> convert)
        {
            foreach (Control child in control.Controls) Recolor(child, convert);
            control.BackColor = convert(control.BackColor);
            control.ForeColor = convert(control.ForeColor);
            var card = control as MaterialCard;
            if (card != null) card.FillColor = convert(card.FillColor);
        }
    }
}
