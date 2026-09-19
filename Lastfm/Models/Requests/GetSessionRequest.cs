namespace Lastfm.Models.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// auth.getSession - exchanges the authorisation token for a session key.
    /// </summary>
    public class GetSessionRequest : BaseRequest
    {
        public string Token { get; set; }

        public override Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>(base.ToDictionary())
            {
                { "token", Token },
            };
        }
    }
}
