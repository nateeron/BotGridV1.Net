using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("UserLoginLogs")]
    public class DbUserLoginLog
    {
        [Key]
        [Column("LogID")]
        public int LogID { get; set; }

        [Column("UserID")]
        public int? UserID { get; set; }

        [Column("Username")]
        [MaxLength(100)]
        public string? Username { get; set; }

        [Column("LoginAt")]
        public DateTime LoginAt { get; set; } = DateTime.UtcNow;

        [Column("IPAddress")]
        [MaxLength(50)]
        public string? IPAddress { get; set; }

        [Column("UserAgent")]
        [MaxLength(500)]
        public string? UserAgent { get; set; }

        [Column("IsSuccess")]
        public bool IsSuccess { get; set; }

        [Column("FailReason")]
        [MaxLength(255)]
        public string? FailReason { get; set; }

        // Navigation property
        [ForeignKey("UserID")]
        public virtual DbUser? User { get; set; }
    }
}
