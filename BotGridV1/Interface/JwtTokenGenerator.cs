using JwtAppLogin.Models;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using API_FMS.Interface;
using System.Collections.Generic;
using System.Linq;

namespace BotGridV1.Interface
{
    public class JwtTokenGenerator : IJwtTokenGenerator
    {
        private readonly IConfiguration _config;

        public JwtTokenGenerator(IConfiguration config)
        {
            _config = config;
        }

        public SetRoles AuthenticateUser(SetRoles loginCredentials)
        {
            List<SetRoles> appUsers = new List<SetRoles>
        {
           new SetRoles {  users = "admin", level = 0 },
           new SetRoles { users = "user", level =1 }
         };

            SetRoles user = appUsers.SingleOrDefault(x => x.users == loginCredentials.users);
            return user;
        }

        public string GenerateJWTToken(SetRoles userInfo, string tokenVersion = null)
        {
            //var keyBytes = Encoding.UTF8.GetBytes("00000000000000000000000000000000");// 12 unit
            var keyBytes = Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]);
            var securityKey = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            
            var claimsList = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userInfo.users),
                new Claim("role", userInfo.level.ToString()),
                new Claim("Sid", userInfo.UserID),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };
            
            // Add token_version claim if provided
            if (!string.IsNullOrWhiteSpace(tokenVersion))
            {
                claimsList.Add(new Claim("token_version", tokenVersion));
            }
            
            // Add email claim if userInfo.users is an email
            if (!string.IsNullOrWhiteSpace(userInfo.users) && userInfo.users.Contains("@"))
            {
                claimsList.Add(new Claim(JwtRegisteredClaimNames.Email, userInfo.users));
                claimsList.Add(new Claim(ClaimTypes.Email, userInfo.users));
            }
            
            var claims = claimsList.ToArray();
            var token = new JwtSecurityToken();
            string setTim = _config["Jwt:setTimeOunt"].ToLower();
            if (setTim == "true")
            {
                int t = Convert.ToInt32(_config["Jwt:TimeOuntToken"]);
                token = new JwtSecurityToken(
                            issuer: _config["Jwt:Issuer"],
                            audience: _config["Jwt:Audience"],
                            claims: claims,
                            expires: DateTime.Now.AddMinutes(t - 5),
                            signingCredentials: credentials
                            );
            }
            else
            {
                token = new JwtSecurityToken(
                            issuer: _config["Jwt:Issuer"],
                            audience: _config["Jwt:Audience"],
                            claims: claims,
                            //expires: DateTime.Now.AddYears(userInfo.expiresMin),
                            expires: DateTime.Now.AddMinutes(userInfo.expiresMin - 5),
                            signingCredentials: credentials
                            );
            }

            var tt = new JwtSecurityTokenHandler().WriteToken(token);

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenWW = tokenHandler.ReadJwtToken(tt);
            string expirationClaim = token.Claims.FirstOrDefault(c => c.Type == "exp")?.Value;
            TimeSpan timeZoneOffset = TimeSpan.FromHours(7);
            var expirationDateTimeUtc = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expirationClaim)).UtcDateTime + timeZoneOffset;
            string formattedDateTime2 = expirationDateTimeUtc.ToString("dd/MM/yyyy HH:mm:ss");


            string fors = formattedDateTime2;


            return tt;

        }


        public ResetTokenResult GenerateResetToken(string email)
        {
            var keyBytes = Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]);
            var securityKey = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var randomKey = GenerateRandomKey(10);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Email, email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("reset_key", randomKey) 
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(3),
                signingCredentials: credentials
            );

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);

            return new ResetTokenResult
            {
                Token = jwt,
                ResetKey = randomKey
            };
        }

        private static string GenerateRandomKey(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var builder = new StringBuilder();

            for (int i = 0; i < length; i++)
            {
                builder.Append(chars[random.Next(chars.Length)]);
            }

            return builder.ToString();
        }

        public string HashPassword(string password)
        {
            // Generate a salt and hash the password
            return BCrypt.Net.BCrypt.HashPassword(password, 12);
        }
       

        public bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
                return false;

            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
    }
}
