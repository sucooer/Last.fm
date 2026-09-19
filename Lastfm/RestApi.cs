namespace Lastfm
{
    using Api;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Controller.Net;
    using MediaBrowser.Model.Serialization;
    using MediaBrowser.Model.Services;
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Step 1 (POST, kept for API / diagnostics) - returns an authorisation URL.
    /// </summary>
    [Route("/Lastfm/GetAuthUrl", "POST")]
    [Unauthenticated]
    public class GetAuthUrlRequest
    {
        public string ApiKey { get; set; }
    }

    /// <summary>
    /// Step 2 (POST, kept for API / diagnostics) - exchanges the token for a session key.
    /// </summary>
    [Route("/Lastfm/CompleteAuth", "POST")]
    [Unauthenticated]
    public class CompleteAuthRequest
    {
        public string Token { get; set; }
    }

    /// <summary>
    /// Save the API key/secret from a plain HTML form POST, then redirect back
    /// to the configuration page. Zero-JS friendly.
    /// </summary>
    [Route("/Lastfm/SaveConfig", "POST")]
    [Unauthenticated]
    public class SaveConfigRequest : IReturnVoid
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
    }

    /// <summary>
    /// Zero-JS authorisation step 1: HTTP 302 redirect to the last.fm
    /// authorisation page. Linked directly from the config page.
    /// </summary>
    [Route("/Lastfm/AuthRedirect", "GET")]
    [Unauthenticated]
    public class AuthRedirectRequest : IReturnVoid
    {
    }

    /// <summary>
    /// Zero-JS authorisation step 2: exchanges the pending token for a session
    /// key and returns a plain HTML result page.
    /// </summary>
    [Route("/Lastfm/CompleteAuthRedirect", "GET")]
    [Unauthenticated]
    public class CompleteAuthRedirectRequest : IReturnVoid
    {
    }

    public class RestApi : IService, IRequiresRequest
    {
        // Token is short-lived and only needed until the user finishes the
        // browser authorisation, so keeping it in memory is fine.
        private static string _pendingToken;

        private readonly LastfmApiClient _apiClient;

        /// <summary>Injected by the service host.</summary>
        public IRequest Request { get; set; }

        private IResponse Response => Request.Response;

        /// <summary>
        /// Once a session key exists the auth endpoints lock themselves to
        /// prevent anonymous reconfiguration. To unlock: remove SessionKey
        /// from plugins/configurations/Lastfm.xml and restart Emby.
        /// </summary>
        private static bool IsLocked
        {
            get
            {
                var c = Plugin.Instance != null ? Plugin.Instance.PluginConfiguration : null;
                return c != null && !string.IsNullOrWhiteSpace(c.SessionKey);
            }
        }

        private const string LockedMessage =
            "当前已完成授权（session key 已存在），授权接口已自动锁定。" +
            "如需重新授权，请删除 plugins/configurations/Lastfm.xml 中的 SessionKey 行并重启 Emby。";

        public RestApi(IJsonSerializer jsonSerializer, IHttpClient httpClient)
        {
            _apiClient = new LastfmApiClient(httpClient, jsonSerializer);
        }

        // ------------------------------------------------------------------
        // JSON API (used by scripts / F12 diagnostics)
        // ------------------------------------------------------------------

        public async Task<object> Post(GetAuthUrlRequest request)
        {
            if (IsLocked) return new { Error = LockedMessage };

            var config = Plugin.Instance.PluginConfiguration;

            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                config.ApiKey = request.ApiKey.Trim();
                Plugin.Instance.SaveConfiguration();
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                return new { Error = "API key 未配置，请先在配置页填写 Last.fm API key" };
            }

            var tokenResponse = await _apiClient.GetAuthToken().ConfigureAwait(false);

            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.Token))
            {
                return new { Error = "获取授权 token 失败: " + (tokenResponse?.Message ?? "网络或 API key 问题") };
            }

            _pendingToken = tokenResponse.Token;

            var authUrl = BuildAuthUrl(config.ApiKey, tokenResponse.Token);

            return new { Token = tokenResponse.Token, Url = authUrl };
        }

        public async Task<object> Post(CompleteAuthRequest request)
        {
            if (IsLocked) return new { Error = LockedMessage };

            var token = !string.IsNullOrWhiteSpace(request.Token) ? request.Token : _pendingToken;

            if (string.IsNullOrWhiteSpace(token))
            {
                return new { Error = "缺少 token，请先点击“获取授权链接”并完成网页授权" };
            }

            var sessionResponse = await _apiClient.GetSession(token).ConfigureAwait(false);

            if (sessionResponse == null || sessionResponse.Session == null)
            {
                return new { Error = "授权失败: " + (sessionResponse?.Message ?? "未知错误") };
            }

            var config = Plugin.Instance.PluginConfiguration;
            config.SessionKey = sessionResponse.Session.Key;
            config.LastfmUsername = sessionResponse.Session.Name;
            Plugin.Instance.SaveConfiguration();

            _pendingToken = null;

            return new { Name = sessionResponse.Session.Name, SessionKey = sessionResponse.Session.Key };
        }

        // ------------------------------------------------------------------
        // Zero-JS endpoints (plain links / form posts from the config page)
        // ------------------------------------------------------------------

        public void Post(SaveConfigRequest request)
        {
            if (IsLocked)
            {
                Response.Redirect("../web/index.html#!/configurationpage?name=lastfm");
                return;
            }

            var config = Plugin.Instance.PluginConfiguration;
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                config.ApiKey = request.ApiKey.Trim();
            if (!string.IsNullOrWhiteSpace(request.ApiSecret))
                config.ApiSecret = request.ApiSecret.Trim();
            Plugin.Instance.SaveConfiguration();

            // Redirect back to the plugin page inside the dashboard.
            // Relative so it works with a custom base URL (/emby/...).
            Response.Redirect("../web/index.html#!/configurationpage?name=lastfm");
        }

        public async Task Get(AuthRedirectRequest request)
        {
            if (IsLocked)
            {
                await WriteHtmlAsync("已授权", LockedMessage).ConfigureAwait(false);
                return;
            }

            var config = Plugin.Instance.PluginConfiguration;

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                await WriteHtmlAsync("尚未配置 API Key", "请回到配置页填写 Last.fm API Key 并保存，然后再点击获取授权链接。").ConfigureAwait(false);
                return;
            }

            var tokenResponse = await _apiClient.GetAuthToken().ConfigureAwait(false);

            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.Token))
            {
                var detail = tokenResponse != null
                    ? "Last.fm 返回 error " + tokenResponse.ErrorCode + ": " + HtmlEncode(tokenResponse.Message)
                    : "网络错误，服务器无法访问 ws.audioscrobbler.com。";
                Plugin.Logger.Error("[Lastfm] AuthRedirect getToken failed: {0}", detail);
                await WriteHtmlAsync("获取授权 token 失败", detail + "<br/>请检查 API Key 是否正确、服务器能否访问 ws.audioscrobbler.com。").ConfigureAwait(false);
                return;
            }

            _pendingToken = tokenResponse.Token;

            var authUrl = BuildAuthUrl(config.ApiKey, tokenResponse.Token);
            Plugin.Logger.Info("[Lastfm] AuthRedirect: token {0}..., redirecting to {1}",
                tokenResponse.Token.Substring(0, Math.Min(8, tokenResponse.Token.Length)), authUrl);

            Response.Redirect(authUrl);
        }

        public async Task Get(CompleteAuthRedirectRequest request)
        {
            if (IsLocked)
            {
                await WriteHtmlAsync("已授权", LockedMessage).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(_pendingToken))
            {
                await WriteHtmlAsync("缺少授权 token", "请先点击「① 获取授权链接」并在 Last.fm 页面完成授权。如果刚重启过 Emby，请重新从①开始。").ConfigureAwait(false);
                return;
            }

            var sessionResponse = await _apiClient.GetSession(_pendingToken).ConfigureAwait(false);

            if (sessionResponse == null || sessionResponse.Session == null)
            {
                var detail = sessionResponse != null
                    ? "Last.fm 返回 error " + sessionResponse.ErrorCode + ": " + HtmlEncode(sessionResponse.Message)
                    : "网络错误或响应解析失败。";
                Plugin.Logger.Error("[Lastfm] CompleteAuth getSession failed: {0}", detail);
                await WriteHtmlAsync("授权失败", detail +
                    "<br/><br/>常见原因：<br/>1. 还没有在 Last.fm 授权页面点击「允许（Authorize）」——请先点①完成网页授权。<br/>" +
                    "2. token 已过期（有效期 60 分钟）或已被使用——请重新从①开始，并在打开 Last.fm 页面后尽快完成授权。<br/>" +
                    "3. Shared Secret 填写错误也会导致换 session 失败，请检查配置。").ConfigureAwait(false);
                return;
            }

            var config = Plugin.Instance.PluginConfiguration;
            config.SessionKey = sessionResponse.Session.Key;
            config.LastfmUsername = sessionResponse.Session.Name;
            Plugin.Instance.SaveConfiguration();

            _pendingToken = null;

            await WriteHtmlAsync("授权成功", "已授权用户：<b>" + HtmlEncode(sessionResponse.Session.Name) + "</b><br/>session key 已保存，之后播放音乐即自动 scrobble。可以关闭本页回到 Emby 配置页查看状态。").ConfigureAwait(false);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static string BuildAuthUrl(string apiKey, string token)
        {
            // NOTE: must be https - last.fm 301-redirects http and the token
            // query parameter can get dropped, showing "token is invalid".
            return "https://www.last.fm/api/auth/?api_key=" + Uri.EscapeDataString(apiKey) +
                   "&token=" + Uri.EscapeDataString(token);
        }

        private async Task WriteHtmlAsync(string title, string bodyHtml)
        {
            var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>" + HtmlEncode(title) + "</title></head>" +
                       "<body style=\"font-family:sans-serif;max-width:36em;margin:4em auto;padding:0 1em;\">" +
                       "<h2>" + HtmlEncode(title) + "</h2><p>" + bodyHtml + "</p>" +
                       "<p><a href=\"../web/index.html#!/configurationpage?name=lastfm\">返回 Last.fm 配置页</a></p>" +
                       "</body></html>";

            Response.ContentType = "text/html; charset=utf-8";
            await Response.OutputWriter.WriteAsync(Encoding.UTF8.GetBytes(html)).ConfigureAwait(false);
            await Response.CompleteAsync().ConfigureAwait(false);
        }

        private static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
