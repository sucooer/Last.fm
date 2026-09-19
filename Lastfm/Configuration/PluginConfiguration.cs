namespace Lastfm.Configuration
{
    using Models;
    using MediaBrowser.Model.Plugins;

    /// <summary>
    /// Class PluginConfiguration
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public LastfmUser[] LastfmUsers { get; set; }

        /// <summary>
        /// Last.fm API application key. Register a free app at
        /// https://www.last.fm/api/account/create
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Last.fm API shared secret, required to sign write requests.
        /// </summary>
        public string ApiSecret { get; set; }

        /// <summary>
        /// Session key obtained via the token authorisation flow
        /// (auth.getToken -> user authorises -> auth.getSession).
        /// </summary>
        public string SessionKey { get; set; }

        /// <summary>
        /// Last.fm username returned by the authorisation flow.
        /// </summary>
        public string LastfmUsername { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration" /> class.
        /// </summary>
        public PluginConfiguration()
        {
            LastfmUsers = new LastfmUser[] { };
        }
    }
}
