using JwtAppLogin.Models;

namespace BotGridV1.Interface
{
    public interface IJwtTokenGenerator
    {
        string GenerateJWTToken(SetRoles userInfo, string tokenVersion = null);
        ResetTokenResult GenerateResetToken(string email);
        SetRoles AuthenticateUser(SetRoles loginCredentials);
        string HashPassword(string password);
        bool VerifyPassword(string password, string hashedPassword);
    }

    public class ResetTokenResult
    {
        public string Token { get; set; }
        public string ResetKey { get; set; }
    }

}
