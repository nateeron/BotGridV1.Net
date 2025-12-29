namespace BotGridV1.Models.Login
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; } // ตัวอย่าง (ควร Hash)
        public string Role { get; set; }
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpireAt { get; set; }
    }
    public class LogoutRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class SignupRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public class SignupResponse
    {
        public int UserID { get; set; }
        public string Username { get; set; }
        public string Message { get; set; }
    }

    public class SetUserRoleRequest
    {
        public int UserID { get; set; }
        public int? RoleID { get; set; }
        public string? RoleCode { get; set; }
    }

    public class SetUserRoleResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int? UserRoleID { get; set; }
        public int UserID { get; set; }
        public int RoleID { get; set; }
        public string RoleCode { get; set; }
        public string RoleName { get; set; }
    }

    public class RemoveUserRoleRequest
    {
        public int UserID { get; set; }
        public int? RoleID { get; set; }
        public string? RoleCode { get; set; }
    }

    public class GetUserRolesResponse
    {
        public bool Success { get; set; }
        public int UserID { get; set; }
        public string Username { get; set; }
        public List<UserRoleInfo> Roles { get; set; } = new List<UserRoleInfo>();
    }

    public class UserRoleInfo
    {
        public int RoleID { get; set; }
        public string RoleCode { get; set; }
        public string RoleName { get; set; }
        public bool IsActive { get; set; }
    }

    public class TokenExpirationResponse
    {
        public bool Success { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int Days { get; set; }
        public int Hours { get; set; }
        public int Minutes { get; set; }
        public int TotalHours { get; set; }
        public int TotalMinutes { get; set; }
        public bool IsExpired { get; set; }
        public string TimeRemaining { get; set; } = string.Empty;
    }

}
