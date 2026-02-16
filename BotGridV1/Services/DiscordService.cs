using System.Net.Http.Json;
using System.Text.Json;
using BotGridV1.Models;

namespace BotGridV1.Services
{
    public class DiscordService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DiscordService> _logger;
        private readonly AlertLogService? _alertLogService;
        private readonly Dictionary<string, DateTime> _lastAlertTimes = new Dictionary<string, DateTime>();
        private readonly object _alertLock = new object();
        private readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(5); // 5 minutes cooldown

        public DiscordService(HttpClient httpClient, ILogger<DiscordService> logger, AlertLogService? alertLogService = null)
        {
            _httpClient = httpClient;
            _logger = logger;
            _alertLogService = alertLogService;
        }

        /// <summary>
        /// Check if alert should be sent (rate limiting - 5 minutes cooldown)
        /// ตรวจสอบว่า alert ควรส่งหรือไม่ (rate limiting - รอ 5 นาที)
        /// </summary>
        private bool ShouldSendAlert(string alertKey)
        {
            lock (_alertLock)
            {
                if (_lastAlertTimes.TryGetValue(alertKey, out var lastTime))
                {
                    if (DateTime.UtcNow - lastTime < _alertCooldown)
                    {
                        // Still in cooldown period
                        // ยังอยู่ในช่วง cooldown
                        return false;
                    }
                }
                
                // Update last alert time
                // อัปเดตเวลาที่ส่ง alert ล่าสุด
                _lastAlertTimes[alertKey] = DateTime.UtcNow;
                return true;
            }
        }

        /// <summary>
        /// Generate alert key for rate limiting
        /// สร้าง key สำหรับ rate limiting
        /// </summary>
        private string GenerateAlertKey(string webhookUrl, string title, string description)
        {
            // Use webhook + title + description as key (normalize description to remove variable parts)
            // ใช้ webhook + title + description เป็น key (ทำให้ description เป็นมาตรฐานเพื่อลบส่วนที่เปลี่ยนแปลง)
            var normalizedDesc = description;
            // Remove variable parts like prices, quantities, etc. to group similar alerts
            // ลบส่วนที่เปลี่ยนแปลง เช่น ราคา, จำนวน เป็นต้น เพื่อจัดกลุ่ม alert ที่คล้ายกัน
            normalizedDesc = System.Text.RegularExpressions.Regex.Replace(normalizedDesc, @"\d+\.?\d*", "X");
            return $"{webhookUrl}_{title}_{normalizedDesc}";
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

            // Check rate limiting
            // ตรวจสอบ rate limiting
            var alertKey = GenerateAlertKey(webhookUrl, title, description);
            if (!ShouldSendAlert(alertKey))
            {
                _logger.LogDebug($"Alert skipped due to cooldown: {title}");
                return false; // Skip sending due to cooldown
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
                
                // Also send to UI via AlertLogService
                if (_alertLogService != null)
                {
                    await _alertLogService.AddLogAsync(new AlertLog
                    {
                        Type = "DISCORD",
                        Level = "Information",
                        Title = title,
                        Message = description,
                        Details = fields != null ? string.Join(", ", fields.Select(f => $"{f.Key}: {f.Value}")) : null,
                        Fields = fields,
                        Color = color,
                        Timestamp = DateTime.UtcNow
                    });
                }
                
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
        public async Task LogErrorAsync(string? webhook1, string? webhook2, string error, string? details = null, string? configId = null)
        {
            var fields = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(details))
            {
                fields.Add("Details", details);
            }

            // Send to UI
            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "ERROR",
                    Level = "Error",
                    Title = "❌ Error",
                    Message = error,
                    Details = details,
                    Fields = fields,
                    Color = 0xe74c3c,
                    ConfigId = configId,
                    Timestamp = DateTime.UtcNow
                });
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
        public async Task LogBuyAsync(string? webhook1, string? webhook2, string symbol, decimal price, decimal quantity, decimal buyAmount, string orderId, string? configId = null)
        {
            var fields = new Dictionary<string, string>
            {
                { "Symbol", symbol },
                { "Price", $"{price:F8}" },
                { "Quantity", $"{quantity:F8}" },
                { "Buy Amount (USD)", $"{buyAmount:F2}" },
                { "Order ID", orderId }
            };

            // Send to UI
            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "BUY",
                    Level = "Information",
                    Title = "🟢 Buy Order Executed",
                    Message = $"Successfully placed buy order for {symbol}",
                    Fields = fields,
                    Color = 0x2ecc71,
                    ConfigId = configId,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
            }

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
        public async Task LogSellAsync(string? webhook1, string? webhook2, string symbol, decimal price, decimal quantity, decimal? profitLoss, string orderId, string? configId = null)
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

            // Send to UI
            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "SELL",
                    Level = "Information",
                    Title = "🔴 Sell Order Executed",
                    Message = $"Successfully placed sell order for {symbol}",
                    Fields = fields,
                    Color = 0xe67e22,
                    ConfigId = configId,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
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

            // Send to UI
            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "START",
                    Level = "Information",
                    Title = "▶️ Bot Started",
                    Message = $"Trading bot started for {symbol}",
                    Fields = fields,
                    Color = 0x3498db,
                    ConfigId = configId.ToString(),
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
            }

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
        public async Task LogStopAsync(string? webhook1, string? webhook2, string? symbol = null, string? configId = null)
        {
            var fields = new Dictionary<string, string>
            {
                { "Status", "STOPPED" }
            };

            if (!string.IsNullOrEmpty(symbol))
            {
                fields.Add("Symbol", symbol);
            }

            // Send to UI
            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "STOP",
                    Level = "Information",
                    Title = "⏹️ Bot Stopped",
                    Message = "Trading bot has been stopped",
                    Fields = fields,
                    Color = 0x95a5a6,
                    ConfigId = configId,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
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
        public async Task LogBuyRetryAsync(string? webhook1, string? webhook2, string symbol, decimal price, int retryCount, string reason, string? configId = null)
        {
            var fields = new Dictionary<string, string>
            {
                { "Symbol", symbol },
                { "Current Price", $"{price:F8}" },
                { "Retry Count", retryCount.ToString() },
                { "Reason", reason }
            };

            // Send to UI
            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "BUY_RETRY",
                    Level = "Warning",
                    Title = "🔄 Buy Retry",
                    Message = $"Retrying buy order for {symbol}",
                    Fields = fields,
                    Color = 0xf39c12,
                    ConfigId = configId,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
            }

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
        public async Task LogBuyNotSuccessAsync(string? webhook1, string? webhook2, string symbol, string error, int retryCount = 0, string? configId = null)
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

            // Send to UI
            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "BUY_FAILED",
                    Level = "Error",
                    Title = "⚠️ Buy Order Failed",
                    Message = $"Buy order failed for {symbol}",
                    Details = error,
                    Fields = fields,
                    Color = 0xe74c3c,
                    ConfigId = configId,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
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

        /// <summary>
        /// Alert: Margin level report (e.g. level &lt;= 1.4, bot stopped).
        /// </summary>
        public async Task LogMarginLevelAlertAsync(string? webhook1, string? webhook2, decimal? marginLevel, string message, string? symbol = null, string? configId = null)
        {
            var fields = new Dictionary<string, string>
            {
                { "Margin Level", marginLevel.HasValue ? marginLevel.Value.ToString("F4") : "N/A" },
                { "Message", message }
            };
            if (!string.IsNullOrEmpty(symbol))
                fields.Add("Symbol", symbol);

            if (_alertLogService != null)
            {
                await _alertLogService.AddLogAsync(new AlertLog
                {
                    Type = "MARGIN_LEVEL",
                    Level = "Warning",
                    Title = "📊 Margin Level Alert",
                    Message = message,
                    Fields = fields,
                    Color = 0xe67e22,
                    ConfigId = configId,
                    Symbol = symbol,
                    Timestamp = DateTime.UtcNow
                });
            }

            await SendToAllWebhooksAsync(
                webhook1,
                webhook2,
                "📊 Margin Level Alert",
                message,
                0xe67e22,
                fields
            );
        }
    }
}

