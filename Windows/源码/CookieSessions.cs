using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TubeGet
{
    internal static class CookieSessions
    {
        internal const string InApp = "site";
        internal static readonly string[] Sources = { null, InApp, "edge", "chrome", "firefox", "brave", "vivaldi", "opera", "chromium" };
        internal static readonly string[] Labels = { "Cookies 文件", "应用内站点登录", "自动读取 Edge", "自动读取 Chrome",
            "自动读取 Firefox", "自动读取 Brave", "自动读取 Vivaldi", "自动读取 Opera", "自动读取 Chromium" };
        private static readonly string[] KnownSites = { "bilibili.com", "xinpianchang.com", "youtube.com", "douyin.com", "behance.net", "vimeo.com", "kuaishou.com" };
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string Store = Path.Combine(Engine.Data, "site-cookies");

        internal static string Label(string source) { return Labels[Math.Max(0, Array.IndexOf(Sources, source))]; }
        internal static bool IsBrowser(string source) { return Array.IndexOf(Sources, source) > 1; }

        internal static bool IsWeb(Uri uri) { return uri != null && uri.IsAbsoluteUri && (uri.Scheme == "https" || uri.Scheme == "http"); }

        internal static string Site(Uri uri)
        {
            if (!IsWeb(uri)) return null;
            string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
            if (host == "b23.tv") return "bilibili.com";
            if (host == "youtu.be") return "youtube.com";
            if (InDomain(host, "iesdouyin.com")) return "douyin.com";
            if (InDomain(host, "gifshow.com") || InDomain(host, "chenzhongtech.com")) return "kuaishou.com";
            return KnownSites.FirstOrDefault(site => InDomain(host, site)) ?? host;
        }

        private static bool InDomain(string host, string domain)
        {
            return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
        }

        private static string PathFor(string site)
        {
            using (var hash = SHA256.Create())
                return Path.Combine(Store, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(site))).Replace("-", "") + ".bin");
        }

        internal static string SavedPath(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return null;
            string site = Site(uri);
            if (site == null) return null;
            string path = PathFor(site);
            return File.Exists(path) ? path : null;
        }

        internal static bool IsSavedPath(string path)
        {
            return !String.IsNullOrWhiteSpace(path) &&
                String.Equals(Path.GetDirectoryName(path), Store, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase);
        }

        internal static void Open(string source, string target)
        {
            byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(source), null, DataProtectionScope.CurrentUser);
            try { File.WriteAllBytes(target, clear); }
            finally { Array.Clear(clear, 0, clear.Length); }
        }

        internal static bool Save(Uri uri, IEnumerable<CoreWebView2Cookie> cookies)
        {
            string site = Site(uri);
            var rows = cookies.Where(cookie => BelongsToSite(site, cookie.Domain) &&
                (cookie.IsSession || cookie.Expires.ToUniversalTime() > DateTime.UtcNow)).Select(cookie =>
            {
                var values = new[] {
                    (cookie.IsHttpOnly ? "#HttpOnly_" : "") + cookie.Domain,
                    cookie.Domain.StartsWith(".", StringComparison.Ordinal) ? "TRUE" : "FALSE",
                    cookie.Path, cookie.IsSecure ? "TRUE" : "FALSE",
                    cookie.IsSession ? "0" : ((long)(cookie.Expires.ToUniversalTime() - Epoch).TotalSeconds).ToString(CultureInfo.InvariantCulture),
                    cookie.Name, cookie.Value
                };
                return values.Any(value => value.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0) ? null : String.Join("\t", values);
            }).Where(row => row != null).ToArray();
            if (rows.Length == 0) return false;

            byte[] clear = Encoding.UTF8.GetBytes("# Netscape HTTP Cookie File\n" + String.Join("\n", rows) + "\n");
            byte[] protectedCookies;
            try { protectedCookies = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(clear, 0, clear.Length); }
            Directory.CreateDirectory(Store);
            string path = PathFor(site);
            string pending = Path.Combine(Store, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(pending, protectedCookies);
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { Engine.TryDelete(pending); }
            return true;
        }

        private static bool BelongsToSite(string site, string cookieDomain)
        {
            string domain = cookieDomain.TrimStart('.').ToLowerInvariant();
            if (site == "kuaishou.com")
                return InDomain(domain, site) || InDomain(domain, "gifshow.com") || InDomain(domain, "chenzhongtech.com");
            if (KnownSites.Contains(site)) return InDomain(domain, site);
            return site == domain || cookieDomain.StartsWith(".", StringComparison.Ordinal) && InDomain(site, domain);
        }
    }

    internal sealed partial class MainForm
    {
        private bool SignInToSite(Form owner, string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !CookieSessions.IsWeb(uri))
            {
                MessageBox.Show(owner, "请先粘贴一个有效的视频网站链接。");
                return false;
            }

            string site = CookieSessions.Site(uri);
            using (var dialog = Dialog("登录 " + site, 950, 660))
            using (var web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Material.Surface })
            {
                var address = new Label {
                    Dock = DockStyle.Top, Height = 32, Padding = new Padding(10, 0, 10, 0),
                    TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "正在打开网站…"
                };
                var footer = Footer();
                footer.Controls.Add(CloseButton(dialog));
                var save = ButtonOf("已登录，获取 Cookies", 164, null, null, null, ButtonStyle.Filled);
                save.Enabled = false;
                footer.Controls.Add(save);
                dialog.Controls.Add(web);
                dialog.Controls.Add(address);
                dialog.Controls.Add(footer);

                web.SourceChanged += (sender, args) =>
                {
                    if (CookieSessions.IsWeb(web.Source))
                        address.Text = web.Source.Scheme + "://" + web.Source.Authority + "    登录完成后，点击下方按钮。";
                };
                save.Click += async (sender, args) =>
                {
                    save.Enabled = false;
                    try
                    {
                        var cookies = await web.CoreWebView2.CookieManager.GetCookiesAsync(null);
                        if (dialog.IsDisposed) return;
                        string loginCookie = site == "bilibili.com" ? "SESSDATA" : site == "vimeo.com" ? "vimeo" : null;
                        if (loginCookie != null && !cookies.Any(cookie =>
                            cookie.Name == loginCookie && !String.IsNullOrEmpty(cookie.Value) &&
                            cookie.Domain.TrimStart('.').Equals(site, StringComparison.OrdinalIgnoreCase)))
                        {
                            string name = site == "vimeo.com" ? "Vimeo" : "B 站";
                            MessageBox.Show(dialog, "还没有检测到 " + name + " 登录。请先在网页中完成登录，再点击获取 Cookies。");
                            return;
                        }
                        if (!CookieSessions.Save(uri, cookies))
                        {
                            MessageBox.Show(dialog, "还没有读到该站点的 Cookies。请完成登录后再试。");
                            return;
                        }
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception)
                    {
                        if (!dialog.IsDisposed) MessageBox.Show(dialog, "读取或保存站点 Cookies 失败，请重试。");
                    }
                    finally { if (!dialog.IsDisposed) save.Enabled = true; }
                };
                dialog.Shown += async (sender, args) =>
                {
                    try
                    {
                        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Engine.Data, "site-browser"), null);
                        if (dialog.IsDisposed) return;
                        await web.EnsureCoreWebView2Async(env);
                        if (dialog.IsDisposed) return;
                        web.CoreWebView2.Settings.IsWebMessageEnabled = false;
                        web.CoreWebView2.NavigationStarting += (view, navigation) =>
                        {
                            Uri destination;
                            if (!Uri.TryCreate(navigation.Uri, UriKind.Absolute, out destination) ||
                                !CookieSessions.IsWeb(destination))
                                navigation.Cancel = true;
                            else save.Enabled = false;
                        };
                        web.CoreWebView2.NewWindowRequested += (view, popup) =>
                        {
                            popup.Handled = true;
                            Uri destination;
                            if (Uri.TryCreate(popup.Uri, UriKind.Absolute, out destination) && CookieSessions.IsWeb(destination))
                                web.Source = destination;
                        };
                        web.CoreWebView2.NavigationCompleted += (view, navigation) =>
                        {
                            if (!dialog.IsDisposed) save.Enabled = true;
                        };
                        web.Source = site == "douyin.com" ? new Uri("https://www.douyin.com/") : uri;
                    }
                    catch (Exception)
                    {
                        if (!dialog.IsDisposed) address.Text = "无法打开应用内浏览器。请确认系统已安装 Microsoft Edge WebView2 Runtime。";
                    }
                };
                return ShowAppDialog(dialog, owner) == DialogResult.OK;
            }
        }
    }
}
