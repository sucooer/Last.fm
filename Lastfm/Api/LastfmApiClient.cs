namespace Lastfm.Api
{
    using MediaBrowser.Common.Net;
    using MediaBrowser.Controller.Entities.Audio;
    using MediaBrowser.Model.Serialization;
    using Models;
    using Models.Requests;
    using Models.Responses;
    using Resources;
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Utils;

    public class LastfmApiClient : BaseLastfmApiClient
    {
        public LastfmApiClient(IHttpClient httpClient, IJsonSerializer jsonSerializer) : base(httpClient, jsonSerializer) { }

        /// <summary>
        /// Step 1 of the token authorisation flow.
        /// auth.getToken - asks Last.fm for a fresh authorisation token.
        /// </summary>
        public async Task<AuthTokenResponse> GetAuthToken()
        {
            var request = new BaseRequest
            {
                ApiKey = GetApiKey(),
                Method = Strings.Methods.GetToken,
                Secure = true
            };

            return await Get<BaseRequest, AuthTokenResponse>(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Step 3 of the token authorisation flow (after the user has authorised
        /// in the browser). auth.getSession - exchanges the token for a session key.
        /// </summary>
        public async Task<MobileSessionResponse> GetSession(string token)
        {
            var request = new GetSessionRequest
            {
                Token = token,
                ApiKey = GetApiKey(),
                Method = Strings.Methods.GetSession,
                Secure = true
            };

            return await Post<GetSessionRequest, MobileSessionResponse>(request).ConfigureAwait(false);
        }

        private static string GetApiKey()
        {
            return Plugin.Instance?.PluginConfiguration?.ApiKey ?? string.Empty;
        }

        public async Task Scrobble(Audio item, LastfmUser user)
        {
            var request = new ScrobbleRequest
            {
                Track      = item.Name,
                Artist     = item.Artists.FirstOrDefault(),
                Timestamp  = Helpers.CurrentTimestamp(),

                ApiKey     = GetApiKey(),
                Method     = Strings.Methods.Scrobble,
                SessionKey = user.SessionKey
            };

            if (!string.IsNullOrWhiteSpace(item.Album))
                request.Album = item.Album;

            if (item.ProviderIds.ContainsKey("MusicBrainzTrack")) 
                request.MbId = item.ProviderIds["MusicBrainzTrack"];

            try
            {
                //Send the request
                var response = await Post<ScrobbleRequest, ScrobbleResponse>(request);

                if (response != null && !response.IsError())
                {
                    Plugin.Logger.Info("{0} played '{1}' - {2} - {3}", user.Username, request.Track, request.Album, request.Artist);
                    return;
                }

                Plugin.Logger.Error("Failed to Scrobble track: {0}", item.Name);
            }
            catch (Exception ex)
            {
                Plugin.Logger.ErrorException("Failed to Scrobble track: {0}", ex, item.Name);
            }
        }

        public async Task NowPlaying(Audio item, LastfmUser user)
        {
            var request = new NowPlayingRequest
            {
                Track  = item.Name,
                Artist = item.Artists.FirstOrDefault(),

                ApiKey = GetApiKey(),
                Method = Strings.Methods.NowPlaying,
                SessionKey = user.SessionKey
            };

            if (!string.IsNullOrWhiteSpace(item.Album)) 
                request.Album = item.Album;

            if (item.ProviderIds.ContainsKey("MusicBrainzTrack"))
                request.MbId = item.ProviderIds["MusicBrainzTrack"];

            //Add duration
            if (item.RunTimeTicks != null)
                request.Duration = Convert.ToInt32(TimeSpan.FromTicks((long)item.RunTimeTicks).TotalSeconds);

            try
            {
                var response = await Post<NowPlayingRequest, ScrobbleResponse>(request);

                if (response != null && !response.IsError())
                {
                    Plugin.Logger.Info("{0} is now playing '{1}' - {2} - {3}", user.Username, request.Track, request.Album, request.Artist);
                    return;
                }

                Plugin.Logger.Error("Failed to send now playing for track: {0}", item.Name);
            }
            catch (Exception ex)
            {
                Plugin.Logger.ErrorException("Failed to send now playing for track: {0}", ex, item.Name);
            }
        }

        /// <summary>
        /// Loves or unloves a track
        /// </summary>
        /// <param name="item">The track</param>
        /// <param name="user">The Lastfm User</param>
        /// <param name="love">If the track is loved or not</param>
        /// <returns></returns>
        public async Task<bool> LoveTrack(Audio item, LastfmUser user, bool love = true)
        {
            var request = new TrackLoveRequest
            {
                Artist = item.Artists.FirstOrDefault(),
                Track  = item.Name,

                ApiKey     = GetApiKey(),
                Method     = love ? Strings.Methods.TrackLove : Strings.Methods.TrackUnlove,
                SessionKey = user.SessionKey,
            };

            try
            {
                //Send the request
                var response = await Post<TrackLoveRequest, BaseResponse>(request);

                if (response != null && !response.IsError())
                {
                    Plugin.Logger.Info("{0} {2}loved track '{1}'", user.Username, item.Name, (love ? "" : "un"));
                    return true;
                }

                Plugin.Logger.Error("{0} Failed to love = {3} track '{1}' - {2}", user.Username, item.Name, response.Message, love);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.ErrorException("{0} Failed to love = {2} track '{1}'", ex, user.Username, item.Name, love);
                return false;
            }
        }

        /// <summary>
        /// Unlove a track. This is the same as LoveTrack with love as false
        /// </summary>
        /// <param name="item">The track</param>
        /// <param name="user">The Lastfm User</param>
        /// <returns></returns>
        public async Task<bool> UnloveTrack(Audio item, LastfmUser user)
        {
            return await LoveTrack(item, user, false);
        }

        public async Task<LovedTracksResponse> GetLovedTracks(LastfmUser user)
        {
            var request = new GetLovedTracksRequest
            {
                User   = user.Username,
                ApiKey = GetApiKey(),
                Method = Strings.Methods.GetLovedTracks
            };

            return await Get<GetLovedTracksRequest, LovedTracksResponse>(request);
        }

        public async Task<GetTracksResponse> GetTracks(LastfmUser user, MusicArtist artist, CancellationToken cancellationToken)
        {
            var request = new GetTracksRequest
            {
                User   = user.Username,
                Artist = artist.Name,
                ApiKey = GetApiKey(),
                Method = Strings.Methods.GetTracks,
                Limit  = 1000
            };

            return await Get<GetTracksRequest, GetTracksResponse>(request, cancellationToken);
        }

        public async Task<GetTracksResponse> GetTracks(LastfmUser user, CancellationToken cancellationToken, int page = 0, int limit = 200)
        {
            var request = new GetTracksRequest
            {
                User   = user.Username,
                ApiKey = GetApiKey(),
                Method = Strings.Methods.GetTracks,
                Limit  = limit,
                Page   = page
            };

            return await Get<GetTracksRequest, GetTracksResponse>(request, cancellationToken);
        }

        public async Task<GetArtistTracksResponse> GetArtistTracks(LastfmUser user, MusicArtist artist,CancellationToken cancellationToken, int page = 0, int limit = 200)
        {
            var request = new GetTracksRequest
            {
                User = user.Username,
                Artist = artist.Name,
                ApiKey = GetApiKey(),
                Method = Strings.Methods.GetArtistTracks,
                Limit = limit,
                Page = page
            };

            return await Get<GetTracksRequest, GetArtistTracksResponse>(request, cancellationToken);
        }
    }
}
