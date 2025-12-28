using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("UserAuthori")]
    public class DbUserAuthori
    {
        [Key]
        [Column("UserID")]
        public int UserID { get; set; }

        [Column("Username")]
        [MaxLength(100)]
        [Required]
        public string Username { get; set; } = string.Empty;

        [Column("PasswordHash")]
        [MaxLength(500)]
        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Column("FullName")]
        [MaxLength(200)]
        public string? FullName { get; set; }

        [Column("Email")]
        [MaxLength(200)]
        public string? Email { get; set; }

        [Column("ConfigID")]
        public int? ConfigID { get; set; }

        [Column("DateCreate")]
        public DateTime DateCreate { get; set; } = DateTime.UtcNow;

        [Column("DateUpdate")]
        public DateTime? DateUpdate { get; set; }

        [Column("IsApprove")]
        public bool IsApprove { get; set; } = false;

        [Column("Authori_ID")]
        public int? Authori_ID { get; set; }

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Column("IsDelete")]
        public bool IsDelete { get; set; } = false;

        // Navigation properties
        public virtual ICollection<DbUserRole> UserRoles { get; set; } = new List<DbUserRole>();
        public virtual ICollection<DbRefreshToken> RefreshTokens { get; set; } = new List<DbRefreshToken>();
    }
}
