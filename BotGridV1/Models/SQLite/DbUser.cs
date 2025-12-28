using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("Users")]
    public class DbUser
    {
        [Key]
        [Column("UserID")]
        public int UserID { get; set; }

        [Column("Username")]
        [MaxLength(100)]
        [Required]
        public string Username { get; set; } = string.Empty;

        [Column("Email")]
        [MaxLength(255)]
        public string? Email { get; set; }

        [Column("PasswordHash")]
        [MaxLength(255)]
        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Column("PasswordSalt")]
        [MaxLength(255)]
        [Required]
        public string PasswordSalt { get; set; } = string.Empty;

        [Column("FullName")]
        [MaxLength(200)]
        public string? FullName { get; set; }

        [Column("PhoneNumber")]
        [MaxLength(50)]
        public string? PhoneNumber { get; set; }

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Column("IsLocked")]
        public bool IsLocked { get; set; } = false;

        [Column("FailedLoginCount")]
        public int FailedLoginCount { get; set; } = 0;

        [Column("LastLoginAt")]
        public DateTime? LastLoginAt { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("UpdatedAt")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public virtual ICollection<DbUserRoleLogin> UserRoles { get; set; } = new List<DbUserRoleLogin>();
        public virtual ICollection<DbUserRefreshTokenLogin> UserRefreshTokens { get; set; } = new List<DbUserRefreshTokenLogin>();
        public virtual ICollection<DbUserLoginLog> UserLoginLogs { get; set; } = new List<DbUserLoginLog>();
    }
}
