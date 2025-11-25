using Binance.Net.Enums;

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

    public class req_BuyNow
    {
        public int? ConfigId { get; set; }
        public decimal? BuyAmountUSD { get; set; } // Optional: override config BuyAmountUSD
        public string? Symbol { get; set; } // Optional: override config SYMBOL
    }

    public class req_SellNow
    {
        public int OrderId { get; set; }
        public int? ConfigId { get; set; } // Optional: for validation
    }

    public class req_GetFilledOrders
    {
        public int? ConfigId { get; set; }
        public string? Symbol { get; set; }
        public string? OrderSide { get; set; } // "BUY", "SELL", null = all
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int? Limit { get; set; } = 100; // Max 1000
        public long? OrderId { get; set; } // Filter by specific order ID
    }

    public class res_FilledOrder
    {
        public long OrderId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // "BUY", "SELL"
        public string Type { get; set; } = string.Empty; // "MARKET", "LIMIT", etc.
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal QuoteQuantity { get; set; } // Total value in quote currency
        public decimal QuantityFilled { get; set; }
        public decimal QuoteQuantityFilled { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? UpdateTime { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class res_GetFilledOrders
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public List<res_FilledOrder> Orders { get; set; } = new();
        public int Total { get; set; }
    }

    public class res_CoinBalance
    {
        public string Coin { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal LatestPrice { get; set; }
        public decimal ValueInUSDT { get; set; }
    }

    public class res_GetAllCoins
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public List<res_CoinBalance> Coins { get; set; } = new();
        public decimal TotalValueUSD { get; set; }
        public int Count { get; set; }
    }

    public class req_Trade
    {
        public int? ConfigId { get; set; }
        public string Symbol { get; set; } = string.Empty; // Required: e.g., "BTCUSDT", "XRPUSDT"
        public string Side { get; set; } = string.Empty; // Required: "BUY" or "SELL"
        public string OrderType { get; set; } = "MARKET"; // "MARKET" or "LIMIT"
        public decimal? Price { get; set; } // Required for LIMIT orders
        
        // Value specification - use ONE of these:
        public decimal? CoinQuantity { get; set; } // Base asset quantity (e.g., 0.1 BTC)
        public decimal? UsdAmount { get; set; } // USD/USDT amount to spend/receive
        public decimal? PortfolioPercent { get; set; } // Percentage of portfolio (0-100)
        
        public TimeInForce? TimeInForce { get; set; } = Binance.Net.Enums.TimeInForce.GoodTillCanceled; // For LIMIT orders
    }

    public class res_Trade
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public long? OrderId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public string OrderType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public decimal? Price { get; set; }
        public decimal? QuantityFilled { get; set; }
        public decimal? QuoteQuantityFilled { get; set; }
        public decimal? ActualPrice { get; set; } // Average fill price
        public decimal? TotalCost { get; set; } // Total cost in quote currency
        public DateTime? CreateTime { get; set; }
        public Dictionary<string, object>? AdditionalInfo { get; set; }
    }

    public class req_GetServerTime
    {
        public int? ConfigId { get; set; }
    }

    public class res_ServerTime
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public DateTime ServerTime { get; set; }
        public DateTime LocalTime { get; set; }
        public long TimeDifferenceMs { get; set; } // Difference in milliseconds (Server - Local)
        public bool IsSynchronized { get; set; } // True if difference is within acceptable range (±1000ms)
        public string? Recommendation { get; set; }
    }
}

