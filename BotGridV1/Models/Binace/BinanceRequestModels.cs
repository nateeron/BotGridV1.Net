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

    public class req_GetPriceRealtime
    {
        public string? Symbol { get; set; } // Default: BTCUSDT
    }

    public class req_StartBot
    {
        public int? ConfigId { get; set; }
    }

    // Discord Logging Request Models
    public class req_DiscordLog
    {
        public string? Webhook1 { get; set; }
        public string? Webhook2 { get; set; }
        public string? Message { get; set; }
        public string? Details { get; set; }
    }

    public class req_DiscordBuyLog
    {
        public string? Webhook1 { get; set; }
        public string? Webhook2 { get; set; }
        public string? Symbol { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal BuyAmount { get; set; }
        public string? OrderId { get; set; }
    }

    public class req_DiscordSellLog
    {
        public string? Webhook1 { get; set; }
        public string? Webhook2 { get; set; }
        public string? Symbol { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal? ProfitLoss { get; set; }
        public string? OrderId { get; set; }
    }

    public class req_DiscordStartLog
    {
        public string? Webhook1 { get; set; }
        public string? Webhook2 { get; set; }
        public string? Symbol { get; set; }
        public int ConfigId { get; set; }
    }

    public class req_DiscordStopLog
    {
        public string? Webhook1 { get; set; }
        public string? Webhook2 { get; set; }
        public string? Symbol { get; set; }
    }

    public class req_DiscordBuyRetryLog
    {
        public string? Webhook1 { get; set; }
        public string? Webhook2 { get; set; }
        public string? Symbol { get; set; }
        public decimal Price { get; set; }
        public int RetryCount { get; set; }
        public string? Reason { get; set; }
    }

    public class req_DiscordBuyNotSuccessLog
    {
        public string? Webhook1 { get; set; }
        public string? Webhook2 { get; set; }
        public string? Symbol { get; set; }
        public string? Error { get; set; }
        public int RetryCount { get; set; }
    }
}

