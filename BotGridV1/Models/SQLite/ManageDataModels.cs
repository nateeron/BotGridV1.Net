using System.ComponentModel.DataAnnotations;

namespace BotGridV1.Models.SQLite
{
    public class GetTableDataRequest
    {
        [Required]
        public string TableName { get; set; } = string.Empty;
        public int? Page { get; set; }
        public int? PageSize { get; set; }
        public Dictionary<string, object>? Filters { get; set; }
    }

    public class GetTableDataResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public List<Dictionary<string, object>>? Data { get; set; }
        public int? TotalCount { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public class DynamicInsertRequest
    {
        [Required]
        public string TableName { get; set; } = string.Empty;
        [Required]
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    }

    public class DynamicUpdateRequest
    {
        [Required]
        public string TableName { get; set; } = string.Empty;
        [Required]
        public Dictionary<string, object> WhereConditions { get; set; } = new Dictionary<string, object>();
        [Required]
        public Dictionary<string, object> UpdateData { get; set; } = new Dictionary<string, object>();
    }

    public class DynamicDeleteRequest
    {
        [Required]
        public string TableName { get; set; } = string.Empty;
        [Required]
        public Dictionary<string, object> WhereConditions { get; set; } = new Dictionary<string, object>();
    }

    public class DynamicOperationResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public int? AffectedRows { get; set; }
    }
}

