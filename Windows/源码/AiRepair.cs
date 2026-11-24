using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TubeGet
{
    public sealed class AiRepairSettings
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string ProtectedKey { get; set; }
        public string ProgramPath { get; set; }
        public string ProgramArguments { get; set; }

        internal static string Protect(string key)
        {
            return key.Length == 0 ? null : Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        }

        internal string ReadKey()
        {
            if (String.IsNullOrEmpty(ProtectedKey)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey),
                    null, DataProtectionScope.CurrentUser));
            }
            catch (FormatException) { return ""; }
            catch (CryptographicException) { return ""; }
        }
    }

    internal static class AiRepair
    {
        internal sealed class Inspection
        {
            internal Dictionary<string, object> Info;
            internal string Url;
            internal bool Repaired;
        }

        internal sealed class Decision
        {
            public string action { get; set; }
            public string issue { get; set; }
        }

        private static readonly Dictionary<string, string> Advice = new Dictionary<string, string> {
            { "link", "链接结构异常。请复制作品页地址后重试。" },
            { "login", "站点可能要求登录。请在「登录选项」刷新站点登录后重试。" },
            { "access", "站点拒绝访问或作品不可用。请先在浏览器确认作品能正常打开。" },
            { "network", "连接异常或请求受限。请检查网络与代理后重试。" },
            { "extractor", "站点解析器可能需要更新。请在「设置 → 组件」检查更新。" },
            { "unknown", "暂时无法定位原因。请确认作品能在浏览器打开，再重新解析。" }
        };

        private const string Instructions =
            "Diagnose a video URL extraction failure using only the supplied structured signals. " +
            "Reply with exactly one JSON object with keys action and issue. " +
            "Select action from the supplied availableActions array. Use none for login, access or " +
            "unsupported sites unless an available local URL repair clearly fits the symptoms. " +
            "For an unsupported URL or missing formats, prefer canonical, then strip_tracking, then single_video if available. " +
            "The retry action repeats the original request and is appropriate for a transient network error. " +
            "Issue must be one of link, login, access, network, extractor, unknown. " +
            "Do not return URLs, code, commands, instructions or any extra keys.";

        internal static async Task<Inspection> Inspect(Options options, string url, bool list, bool autoMode, bool sidecars,
            CancellationToken token, string password, Action<string> progress, Action<string> diagnostics = null)
        {
            var settings = options.AiRepair;
            Uri source;
            bool enabled = settings != null && settings.Enabled && settings.Provider != "doubao-desktop" &&
                Uri.TryCreate(url, UriKind.Absolute, out source) && CookieSessions.IsWeb(source);
            Exception failure;
            try
            {
                var info = await Engine.Inspect(options, url, list, token, password, diagnostics: diagnostics);
                if (!enabled || IsUsable(info, sidecars)) return new Inspection { Info = info, Url = url };
                failure = new InvalidDataException("no formats");
            }
            catch (OperationCanceledException) { throw; }
            catch (MediaAccessException) { throw; }
            catch (Exception ex)
            {
                if (!enabled) throw;
                failure = ex;
            }

            token.ThrowIfCancellationRequested();
            progress("解析失败，正在用 AI 诊断…");
            source = new Uri(url);
            string site = CookieSessions.Site(source);
            var candidates = BuildCandidates(source, autoMode);
            string issue = Classify(failure);
            if (issue == "network") candidates.Add("retry", url);
            var context = new {
                site = new[] { "youtube.com", "bilibili.com", "douyin.com", "xinpianchang.com" }.Contains(site) ? site : "other",
                errorCategory = issue, collectionMode = list, subtitleOrCoverOnly = sidecars,
                availableActions = candidates.Keys.Concat(new[] { "none" }).ToArray()
            };

            Decision decision;
            try
            {
                decision = await Diagnose(settings, options.Proxy, context, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException ex) { throw new InvalidOperationException("AI 诊断失败 · " + ex.Message, failure); }

            token.ThrowIfCancellationRequested();
            string target;
            if (decision.action == null || !candidates.TryGetValue(decision.action, out target))
                throw new InvalidOperationException("AI 诊断 · " + Diagnosis(decision, issue), failure);

            progress("AI 已找到修复方式，正在重新解析…");
            try
            {
                bool useList = list && decision.action != "single_video";
                var info = await Engine.Inspect(options, target, useList, token, password, diagnostics: diagnostics);
                if (!IsUsable(info, sidecars)) throw new InvalidDataException("no formats");
                return new Inspection { Info = info, Url = target, Repaired = true };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("AI 重试后仍解析失败 · " + Advice[Classify(ex)], ex);
            }
        }

        private static string Diagnosis(Decision decision, string localIssue)
        {
            string result;
            return decision.issue != null && decision.issue != "unknown" && Advice.TryGetValue(decision.issue, out result)
                ? result : Advice[localIssue];
        }

        internal static bool IsUsable(Dictionary<string, object> info, bool sidecars)
        {
            if (info == null) return false;
            if (Engine.IsCollectionResult(info)) return Engine.Rows(info, "entries").Any();
            if (sidecars) return Engine.Value(info, "thumbnail").Length > 0 ||
                Engine.Rows(info, "thumbnails").Any() || info.ContainsKey("subtitles") || info.ContainsKey("automatic_captions");
            return Engine.Rows(info, "formats").Any(format => Engine.Value(format, "format_id").Length > 0 &&
                (Engine.Value(format, "vcodec") != "none" || Engine.Value(format, "acodec") != "none"));
        }

        internal static string Classify(Exception failure)
        {
            string message = failure.Message ?? "";
            if (Regex.IsMatch(message, @"cookies?|登录|sign.?in|log.?in|authentication", RegexOptions.IgnoreCase)) return "login";
            if (Regex.IsMatch(message, @"\b(?:429|50[0234])\b|timed? ?out|timeout|connection", RegexOptions.IgnoreCase)) return "network";
            if (Regex.IsMatch(message, @"\b(?:403|404)\b|geo.restrict|private|unavailable|forbidden|not available", RegexOptions.IgnoreCase)) return "access";
            if (Regex.IsMatch(message, @"proxy|network", RegexOptions.IgnoreCase)) return "network";
            if (Regex.IsMatch(message, @"unsupported url|invalid url|not a valid url", RegexOptions.IgnoreCase)) return "link";
            if (Regex.IsMatch(message, @"extract|no formats|no video|unable to|not supported", RegexOptions.IgnoreCase)) return "extractor";
            return "unknown";
        }

        internal static Dictionary<string, string> BuildCandidates(Uri source, bool autoMode)
        {
            var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
            if (source.UserInfo.Length != 0 || !source.IsDefaultPort) return candidates;
            string host = source.IdnHost.ToLowerInvariant();
            string path = source.AbsolutePath;
            string query = source.Query.TrimStart('?');
            var pieces = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            var parameters = System.Web.HttpUtility.ParseQueryString(source.Query);
            string canonical = null;
            bool video = false;
            if (host == "www.douyin.com" || host == "douyin.com")
            {
                string modal = parameters["modal_id"] ?? "";
                video = Regex.IsMatch(path, @"^/video/[0-9]{15,22}/?$");
                if ((path == "/jingxuan" || path == "/jingxuan/" || path == "/") &&
                    Regex.IsMatch(modal, @"^[0-9]{15,22}$"))
                    canonical = "https://www.douyin.com/video/" + modal;
            }
            else if (host == "youtu.be")
            {
                string id = path.Trim('/');
                video = Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$");
                if (video)
                    canonical = "https://www.youtube.com/watch?v=" + id + (query.Length == 0 ? "" : "&" + query);
            }
            else if (host == "www.youtube.com" || host == "m.youtube.com" || host == "youtube.com")
                video = path == "/watch" && Regex.IsMatch(parameters["v"] ?? "", @"^[A-Za-z0-9_-]{11}$");
            else if (host == "www.bilibili.com" || host == "m.bilibili.com" || host == "bilibili.com")
                video = Regex.IsMatch(path, @"^/video/(?:BV[A-Za-z0-9]+|av[0-9]+)/?$");
            else if (host != "space.bilibili.com" && host != "b23.tv" && host != "v.douyin.com" &&
                host != "www.xinpianchang.com" && host != "xinpianchang.com")
                return candidates;

            if (canonical != null) candidates.Add("canonical", canonical);
            var retained = pieces.Where(piece => !Regex.IsMatch(piece.Split('=')[0],
                @"^(utm_[a-z_]+|fbclid|gclid|spm_id_from|vd_source|si)$", RegexOptions.IgnoreCase)).ToArray();
            if (retained.Length < pieces.Length)
            {
                string safeQuery = String.Join("&", retained);
                var builder = new UriBuilder(source) { Query = safeQuery };
                candidates.Add("strip_tracking", builder.Uri.AbsoluteUri);
            }
            if (autoMode && video && parameters["list"] == null && parameters["season_id"] == null && parameters["p"] == null)
                candidates.Add("single_video", source.AbsoluteUri);
            return candidates;
        }

        private static async Task<Decision> Diagnose(AiRepairSettings settings, string proxy, object context, CancellationToken token)
        {
            if (LocalAi.IsLocalProvider(settings.Provider))
            {
                string prompt = Instructions + " Answer from the supplied data without using tools.\n" + Engine.Json().Serialize(context);
                return ParseDecision(await LocalAi.Run(settings, prompt, token).ConfigureAwait(false));
            }
            return await Request(settings.Provider, settings.Model, settings.ReadKey(), proxy, context, token).ConfigureAwait(false);
        }

        internal static async Task Test(AiRepairSettings settings, string proxy, CancellationToken token)
        {
            var decision = await Diagnose(settings, proxy, new {
                site = "other", errorCategory = "unknown", availableActions = new[] { "none" }
            }, token);
            if (decision.action != "none") throw new InvalidOperationException("模型返回了无效的诊断结果。");
        }

        private static async Task<Decision> Request(string provider, string model, string key, string proxy, object context,
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(key) || String.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("请在「设置 → AI 修复」填写模型 ID 和 API Key。");
            string endpoint = provider == "doubao" ? "https://ark.cn-beijing.volces.com/api/v3/chat/completions" :
                "https://api.openai.com/v1/chat/completions";
            var payload = Encoding.UTF8.GetBytes(Engine.Json().Serialize(new {
                model = model, messages = new[] {
                    new { role = "system", content = Instructions },
                    new { role = "user", content = Engine.Json().Serialize(context) }
                }
            }));
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(35));
                try
                {
                    return await Task.Run(() => Send(endpoint, key, proxy, payload, timeout.Token), timeout.Token).ConfigureAwait(false);
                }
                catch (WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (timeout.IsCancellationRequested) throw new InvalidOperationException("模型响应超时。");
                    using (var response = ex.Response as HttpWebResponse)
                    {
                        if (response == null) throw new InvalidOperationException("无法连接模型服务，请检查网络与代理。");
                        int status = (int)response.StatusCode;
                        if (status == 401 || status == 403) throw new InvalidOperationException("模型服务拒绝访问，请检查 API Key 和模型权限。");
                        if (status == 429) throw new InvalidOperationException("模型服务限流或额度不足。");
                        if (status == 400 || status == 404) throw new InvalidOperationException("模型请求无效，请检查模型 ID。");
                        throw new InvalidOperationException("模型服务返回 HTTP " + status + "。");
                    }
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("模型响应超时。");
                }
                catch (ArgumentException) { throw new InvalidOperationException("模型响应无效，请检查配置。"); }
                catch (IOException) { throw new InvalidOperationException("读取模型响应失败。"); }
            }
        }

        private static Decision Send(string endpoint, string key, string proxy, byte[] payload, CancellationToken token)
        {
            if (ServicePointManager.SecurityProtocol != 0)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            request.ContentLength = payload.Length;
            request.AllowAutoRedirect = false;
            request.Proxy = ComponentUpdater.GetProxy(proxy);
            request.Timeout = 35000;
            request.ReadWriteTimeout = 35000;
            using (token.Register(request.Abort))
            {
                token.ThrowIfCancellationRequested();
                using (var stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("模型服务返回了无效响应。");
                    var buffer = new char[128 * 1024];
                    int count = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (count == buffer.Length) throw new InvalidOperationException("模型响应过长。");
                    return Parse(new string(buffer, 0, count));
                }
            }
        }

        internal static Decision Parse(string response)
        {
            var result = Engine.Json().Deserialize<Dictionary<string, object>>(response);
            var first = result == null ? null : Engine.Rows(result, "choices").FirstOrDefault();
            object message;
            var value = first != null && first.TryGetValue("message", out message) ? message as Dictionary<string, object> : null;
            return ParseDecision(value == null ? "" : Engine.Value(value, "content"));
        }

        private static Decision ParseDecision(string content)
        {
            content = (content ?? "").Trim();
            var fenced = Regex.Match(content, @"^\x60{3}(?:json)?\s*(.*?)\s*\x60{3}$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (fenced.Success) content = fenced.Groups[1].Value;
            if (content.Length < 2 || content.Length > 1024 || content[0] != '{' || content[content.Length - 1] != '}')
                throw new InvalidOperationException("模型未返回可用的诊断结果。");
            Decision decision;
            try { decision = Engine.Json().Deserialize<Decision>(content); }
            catch (ArgumentException) { throw new InvalidOperationException("模型未返回可用的诊断结果。"); }
            if (decision == null || !new[] { "none", "canonical", "strip_tracking", "single_video", "retry" }.Contains(decision.action) ||
                decision.issue == null || !Advice.ContainsKey(decision.issue))
                throw new InvalidOperationException("模型返回了不受支持的诊断结果。");
            return decision;
        }
    }
}
