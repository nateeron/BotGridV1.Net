using System.Text.Json.Serialization;

namespace BotGridV1.Models.SQLite
{
    public class DatabaseBackupDto
    {
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
        public List<DbSetting> Settings { get; set; } = new();
        public List<DbOrder> Orders { get; set; } = new();
    }

    public class DatabaseImportRequest
    {
        public bool ReplaceExisting { get; set; } = true;

        [JsonPropertyName("backup")]
        public DatabaseBackupDto? Backup { get; set; }
    }
}

