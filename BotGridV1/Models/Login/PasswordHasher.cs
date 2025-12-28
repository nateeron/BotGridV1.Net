using BCrypt.Net;
using System.Security.Cryptography;

namespace BotGridV1.Models.Login
{
    public static class PasswordHasher
    {
        /// <summary>
        /// Hash a password using BCrypt and generate a salt
        /// </summary>
        public static (string Hash, string Salt) HashPasswordWithSalt(string password)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(password);
            // Generate a random salt for database storage (BCrypt includes salt in hash, but we need separate field)
            var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            return (hash, salt);
        }

        /// <summary>
        /// Hash a password using BCrypt
        /// </summary>
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        /// <summary>
        /// Verify a password against a hash
        /// </summary>
        public static bool VerifyPassword(string password, string passwordHash)
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }

        /// <summary>
        /// Encrypt a password (alias for HashPassword for consistency)
        /// </summary>
        public static string Encrypt(string password)
        {
            return HashPassword(password);
        }

        /// <summary>
        /// Decrypt is not applicable for hashed passwords (one-way function)
        /// This method is provided for API compatibility but will throw an exception
        /// </summary>
        public static string Decrypt(string passwordHash)
        {
            throw new InvalidOperationException("Password hashes cannot be decrypted. Use VerifyPassword to verify a password against a hash.");
        }
    }
}
