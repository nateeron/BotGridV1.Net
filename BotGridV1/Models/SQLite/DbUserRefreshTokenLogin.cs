using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("UserRefreshTokens")]
    public class DbUserRefreshTokenLogin
    {
        [Key]
        [Column("TokenID")]
        public int TokenID { get; set; }

        [Column("UserID")]
        [Required]
        public int UserID { get; set; }

        [Column("RefreshToken")]
        [MaxLength(500)]
        [Required]
        public string RefreshToken { get; set; } = string.Empty;

        [Column("ExpiredAt")]
        [Required]
        public DateTime ExpiredAt { get; set; }

        [Column("IsRevoked")]
        public bool IsRevoked { get; set; } = false;

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("UserID")]
        public virtual DbUser? User { get; set; }
    }
}
