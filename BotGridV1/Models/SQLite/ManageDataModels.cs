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

    /// <summary>
    /// Request model for inserting a single order into db_Order
    /// </summary>
    public class InsertOrderRequest
    {
        public DateTime Timestamp { get; set; }
        public string? OrderBuyID { get; set; }
        public decimal? PriceBuy { get; set; }
        public decimal? PriceWaitSell { get; set; }
        public string? OrderSellID { get; set; }
        public decimal? PriceSellActual { get; set; }
        public decimal? ProfitLoss { get; set; }
        public DateTime? DateBuy { get; set; }
        public DateTime? DateSell { get; set; }
        public int Setting_ID { get; set; }
        public string Status { get; set; } = "WAITING_SELL";
        public string? Symbol { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? BuyAmountUSD { get; set; }
        public decimal? CoinQuantity { get; set; }
    }

    /// <summary>
    /// Response for InsertOrder API
    /// </summary>
    public class InsertOrderResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public int? Id { get; set; }
    }
}

