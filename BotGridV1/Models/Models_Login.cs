using System.Security.Claims;

namespace BotGridV1.Models
{
    public class Model_Login
    {
    }
    public class SendMain
    {
        public string email { get; set; }
    }
    public class reqCtok
    {
        public string? tokens { get; set; }
    }
    public class resp_checkToken
    {
        
        public string sub { get; set; }
        public string sid { get; set; }
        public string role { get; set; }
        public string exp_Timestamp { get; set; }
        public string dateTimeNow { get; set; }
        public string expiration { get; set; }
        public bool tokenExpired { get; set; }
        public string iss { get; set; }
        public string aud { get; set; }

    }

}
