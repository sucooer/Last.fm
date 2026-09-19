namespace Lastfm.Models.Responses
{
    using System.Runtime.Serialization;

    [DataContract]
    public class AuthTokenResponse : BaseResponse
    {
        [DataMember(Name = "token")]
        public string Token { get; set; }
    }
}
