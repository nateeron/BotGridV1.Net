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
        public DateTime ExpireAt { get; set; }
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

}
