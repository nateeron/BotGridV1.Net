using System.Net.Http.Json;
using System.Text.Json;

namespace BotGridV1.Services
{
    public class DiscordService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DiscordService> _logger;

        public DiscordService(HttpClient httpClient, ILogger<DiscordService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Send message to Discord webhook
        /// </summary>
        public async Task<bool> SendWebhookAsync(string webhookUrl, string title, string description, int color = 0x3498db, Dictionary<string, string>? fields = null)
        {
            if (string.IsNullOrEmpty(webhookUrl))
            {
                return false;
            }

            try
            {
                var embed = new
                {
                    title = title,
                    description = description,
                    color = color,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    fields = fields?.Select(f => new { name = f.Key, value = f.Value, inline = true }).ToArray() ?? Array.Empty<object>()
                };

                var payload = new
                {
                    embeds = new[] { embed }
                };

                var response = await _httpClient.PostAsJsonAsync(webhookUrl, payload);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send Discord webhook: {webhookUrl}");
                return false;
            }
        }

        /// <summary>
        /// Send message to multiple Discord webhooks
        /// </summary>
        public async Task SendToAllWebhooksAsync(string? webhook1, string? webhook2, string title, string description, int color = 0x3498db, Dictionary<string, string>? fields = null)
        {
            var tasks = new List<Task>();

            if (!string.IsNullOrEmpty(webhook1))
            {
                tasks.Add(SendWebhookAsync(webhook1, title, description, color, fields));
            }

            if (!string.IsNullOrEmpty(webhook2))
            {
                tasks.Add(SendWebhookAsync(webhook2, title, description, color, fields));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Log Error to Discord
        /// </summary>
        public async Task LogErrorAsync(string? webhook1, string? webhook2, string error, string? details = null)
        {
            var fields = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(details))
            {
                fields.Add("Details", details);
            }

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "❌ Error",
                error,
                0xe74c3c, // Red color
                fields
            );
        }

        /// <summary>
        /// Log Buy action to Discord
        /// </summary>
        public async Task LogBuyAsync(string? webhook1, string? webhook2, string symbol, decimal price, decimal quantity, decimal buyAmount, string orderId)
        {
            var fields = new Dictionary<string, string>
            {
                { "Symbol", symbol },
                { "Price", $"{price:F8}" },
                { "Quantity", $"{quantity:F8}" },
                { "Buy Amount (USD)", $"{buyAmount:F2}" },
                { "Order ID", orderId }
            };

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "🟢 Buy Order Executed",
                $"Successfully placed buy order for {symbol}",
                0x2ecc71, // Green color
                fields
            );
        }

        /// <summary>
        /// Log Sell action to Discord
        /// </summary>
        public async Task LogSellAsync(string? webhook1, string? webhook2, string symbol, decimal price, decimal quantity, decimal? profitLoss, string orderId)
        {
            var fields = new Dictionary<string, string>
            {
                { "Symbol", symbol },
                { "Sell Price", $"{price:F8}" },
                { "Quantity", $"{quantity:F8}" },
                { "Order ID", orderId }
            };

            if (profitLoss.HasValue)
            {
                var profitEmoji = profitLoss.Value >= 0 ? "📈" : "📉";
                fields.Add("Profit/Loss", $"{profitEmoji} {profitLoss.Value:F8}");
            }

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "🔴 Sell Order Executed",
                $"Successfully placed sell order for {symbol}",
                0xe67e22, // Orange color
                fields
            );
        }

        /// <summary>
        /// Log Start action to Discord
        /// </summary>
        public async Task LogStartAsync(string? webhook1, string? webhook2, string symbol, int configId)
        {
            var fields = new Dictionary<string, string>
            {
                { "Symbol", symbol },
                { "Config ID", configId.ToString() },
                { "Status", "RUNNING" }
            };

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "▶️ Bot Started",
                $"Trading bot started for {symbol}",
                0x3498db, // Blue color
                fields
            );
        }

        /// <summary>
        /// Log Stop action to Discord
        /// </summary>
        public async Task LogStopAsync(string? webhook1, string? webhook2, string? symbol = null)
        {
            var fields = new Dictionary<string, string>
            {
                { "Status", "STOPPED" }
            };

            if (!string.IsNullOrEmpty(symbol))
            {
                fields.Add("Symbol", symbol);
            }

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "⏹️ Bot Stopped",
                "Trading bot has been stopped",
                0x95a5a6, // Gray color
                fields
            );
        }

        /// <summary>
        /// Log Buy Retry action to Discord
        /// </summary>
        public async Task LogBuyRetryAsync(string? webhook1, string? webhook2, string symbol, decimal price, int retryCount, string reason)
        {
            var fields = new Dictionary<string, string>
            {
                { "Symbol", symbol },
                { "Current Price", $"{price:F8}" },
                { "Retry Count", retryCount.ToString() },
                { "Reason", reason }
            };

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "🔄 Buy Retry",
                $"Retrying buy order for {symbol}",
                0xf39c12, // Orange color
                fields
            );
        }

        /// <summary>
        /// Log Buy Not Success to Discord
        /// </summary>
        public async Task LogBuyNotSuccessAsync(string? webhook1, string? webhook2, string symbol, string error, int retryCount = 0)
        {
            var fields = new Dictionary<string, string>
            {
                { "Symbol", symbol },
                { "Error", error }
            };

            if (retryCount > 0)
            {
                fields.Add("Retry Count", retryCount.ToString());
            }

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "⚠️ Buy Order Failed",
                $"Buy order failed for {symbol}",
                0xe74c3c, // Red color
                fields
            );
        }
    }
}

