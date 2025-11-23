namespace BotGridV1.Models.Binace
{
    public class req_GetSpotReport
    {
        public int? ConfigId { get; set; }
        public string? Period { get; set; } // 1M, 2M, 3M, 1Y, 2Y, 3Y
    }

    public class req_OpenOrder
    {
        public int? ConfigId { get; set; }
        public string OrderType { get; set; } = string.Empty; // "BUY", "SELL", "BUY_LIMIT_SELL"
        public string Symbol { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public decimal? Price { get; set; } // For limit orders
        public decimal? LimitPrice { get; set; } // For BUY_LIMIT_SELL - the sell limit price
    }

    public class req_CancelOrder
    {
        public int? ConfigId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public long? OrderId { get; set; }
        public string? OrderSide { get; set; } // "BUY", "SELL"
    }

    public class res_SpotReport
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public decimal? PortfolioValue { get; set; }
        public int? OrdersWaiting { get; set; }
        public int? OrdersSuccess { get; set; }
        public string? Period { get; set; }
        public Dictionary<string, object>? AdditionalData { get; set; }
    }

    public class res_OpenOrder
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public long? OrderId { get; set; }
        public string? Symbol { get; set; }
        public string? OrderType { get; set; }
        public string? Status { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Price { get; set; }
    }

    public class res_CancelOrder
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public long? OrderId { get; set; }
        public string? Symbol { get; set; }
    }
}

