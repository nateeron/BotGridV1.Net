using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("db_alert")]
    public class DbAlert
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("alert_id")]
        [MaxLength(100)]
        public string AlertId { get; set; } = Guid.NewGuid().ToString();

        [Column("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Column("type")]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty; // "LOG", "DISCORD", "ERROR", "BUY", "SELL", "START", "STOP"

        [Column("level")]
        [MaxLength(50)]
        public string Level { get; set; } = string.Empty; // "Information", "Warning", "Error", "Debug"

        [Column("title")]
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [Column("details")]
        public string? Details { get; set; }

        [Column("fields_json")]
        public string? FieldsJson { get; set; } // JSON string for Dictionary<string, string>

        [Column("color")]
        public int? Color { get; set; } // Discord color code

        [Column("config_id")]
        [MaxLength(50)]
        public string? ConfigId { get; set; }

        [Column("symbol")]
        [MaxLength(100)]
        public string? Symbol { get; set; }

        [Column("is_read")]
        public bool IsRead { get; set; } = false;

        [Column("read_at")]
        public DateTime? ReadAt { get; set; }
    }
}

