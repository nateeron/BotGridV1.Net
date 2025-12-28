using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("RefreshTokens")]
    public class DbRefreshToken
    {
        [Key]
        [Column("RefreshTokenId")]
        public int RefreshTokenId { get; set; }

        [Column("UserId")]
        [Required]
        public int UserId { get; set; }

        [Column("Token")]
        [MaxLength(500)]
        [Required]
        public string Token { get; set; } = string.Empty;

        [Column("ExpireAt")]
        [Required]
        public DateTime ExpireAt { get; set; }

        [Column("IsRevoked")]
        public bool IsRevoked { get; set; } = false;

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("UserId")]
        public virtual DbUserAuthori? User { get; set; }
    }
}
