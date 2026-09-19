namespace Lastfm
{
    using Api;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Model.Serialization;
    using MediaBrowser.Model.Services;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Step 1 - returns an authorisation URL the user opens in a browser.
    /// </summary>
    [Route("/Lastfm/GetAuthUrl", "POST")]
    public class GetAuthUrlRequest
    {
        public string ApiKey { get; set; }
    }

    /// <summary>
    /// Step 2 - exchanges the (pending) authorisation token for a session key.
    /// </summary>
    [Route("/Lastfm/CompleteAuth", "POST")]
    public class CompleteAuthRequest
    {
        public string Token { get; set; }
    }

    public class RestApi : IService
    {
        // Token is short-lived and only needed until the user finishes the
        // browser authorisation, so keeping it in memory is fine.
        private static string _pendingToken;

        private readonly LastfmApiClient _apiClient;

        public RestApi(IJsonSerializer jsonSerializer, IHttpClient httpClient)
        {
            _apiClient = new LastfmApiClient(httpClient, jsonSerializer);
        }

        public async Task<object> Post(GetAuthUrlRequest request)
        {
            var config = Plugin.Instance.PluginConfiguration;

            // Allow the config page to send the API key along so we can persist
            // it even before the user clicks "save".
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

            var authUrl = "http://www.last.fm/api/auth/?api_key=" + Uri.EscapeDataString(config.ApiKey) +
                          "&token=" + Uri.EscapeDataString(tokenResponse.Token);

            return new { Token = tokenResponse.Token, Url = authUrl };
        }

        public async Task<object> Post(CompleteAuthRequest request)
        {
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
    }
}
