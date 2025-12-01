using System.Security.Claims;
using System;
using BotGridV1.Services;
using System.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;

namespace BotGridV1.Controllers.Services
{
    public class TokenVersionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _config;

        public TokenVersionMiddleware(RequestDelegate next, IConfiguration config)
        {
            _next = next;
            _config = config;
        }

        public async Task Invoke(HttpContext context)
        {
            var email = context.User.FindFirst(ClaimTypes.Email)?.Value;
            var jwtVersion = context.User.FindFirst("token_version")?.Value;

            if (email != null && jwtVersion != null)
            {
                // Resolve scoped service from request services
                var fnSetData = context.RequestServices.GetRequiredService<FN_SetData>();
                
                // อ่าน connection string
                string encryptedCon = _config["ConnectionStrings:DefaultConnection"];
                
                // Decode connection string
                string connectionString = await fnSetData.Decode(encryptedCon);

                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    var cmd = new SqlCommand(@"
                    SELECT [Token_version]
                    FROM [FMS_API].[dbo].[Registration]
                    WHERE UserLogin = @Email", conn);

                    cmd.Parameters.AddWithValue("@Email", email);

                    var dbVersion = await cmd.ExecuteScalarAsync();
                    string dbVersionString = dbVersion == null || dbVersion == DBNull.Value ? null : dbVersion.ToString();

                    if (string.IsNullOrWhiteSpace(dbVersionString) || dbVersionString != jwtVersion)
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync("Token invalidated by logout.");
                        return;
                    }
                }
            }

            await _next(context);
        }
    }

}
