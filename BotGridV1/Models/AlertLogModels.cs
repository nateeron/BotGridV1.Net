namespace BotGridV1.Models
{
    public class AlertLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Type { get; set; } = string.Empty; // "LOG", "DISCORD", "ERROR", "BUY", "SELL", "START", "STOP"
        public string Level { get; set; } = string.Empty; // "Information", "Warning", "Error", "Debug"
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public Dictionary<string, string>? Fields { get; set; }
        public int? Color { get; set; } // Discord color code
        public string? ConfigId { get; set; }
        public string? Symbol { get; set; }
        public bool IsRead { get; set; } = false; // Read status
        public DateTime? ReadAt { get; set; } // When it was marked as read
    }

    public class AlertLogRequest
    {
        public int? Limit { get; set; } = 100;
        public int? Offset { get; set; } = 0;
        public string? Type { get; set; }
        public string? Level { get; set; }
        public string? ConfigId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public bool? IsRead { get; set; } // Filter by read status: true = read, false = unread, null = all
    }

    public class AlertLogResponse
    {
        public List<AlertLog> Logs { get; set; } = new();
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }
}

