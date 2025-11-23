using Binance.Net.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BotGridV1.Services;
using BotGridV1.Models.Binace;
using BotGridV1.Models.SQLite;
using Microsoft.EntityFrameworkCore;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class BotWorkerController : ControllerBase
    {
        private readonly BotWorkerService _botWorkerService;
        private readonly ILogger<BotWorkerController> _logger;
        private readonly IServiceProvider _serviceProvider;
        private BinanceSocketClient? _socketClient;
        private Dictionary<string, System.Threading.CancellationTokenSource> _subscriptions = new();

        public BotWorkerController(BotWorkerService botWorkerService, ILogger<BotWorkerController> logger, IServiceProvider serviceProvider)
        {
            _botWorkerService = botWorkerService;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Get real-time price using WebSocket for Spot API
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetPriceRealtime(req_GetPriceRealtime req)
        {
            try
            {
                var symbol = req.Symbol ?? "XRPUSDT";

                // Cancel existing subscription for this symbol if any
                if (_subscriptions.ContainsKey(symbol))
                {
                    _subscriptions[symbol].Cancel();
                    _subscriptions.Remove(symbol);
                }

                _socketClient = new BinanceSocketClient();
                var cts = new System.Threading.CancellationTokenSource();
                _subscriptions[symbol] = cts;

                var sub = await _socketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(
                    symbol,
                    data =>
                    {
                        // This will be called on each trade update
                        // In a real scenario, you might want to use SignalR or similar for real-time updates to client
                        Console.WriteLine($"[{data.Data.Symbol}] Price: {data.Data.Price}  Qty:{data.Data.Quantity}  Time:{data.Data.TradeTime}");
                    },
                    cts.Token);

                if (!sub.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Subscription failed: {sub.Error?.Message}"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = $"Subscribed to real-time price updates for {symbol}",
                    symbol = symbol
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPriceRealtime");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Start the bot worker
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Start(req_StartBot req)
        {
            try
            {
                // Check if bot is already running
                if (_botWorkerService.IsRunning)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Bot worker is already running",
                        status = "RUNNING",
                        error = "Bot is already started. Please stop it first."
                    });
                }

                // Validate configuration before starting
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var config = req.ConfigId.HasValue
                    ? await context.DbSettings.FindAsync(req.ConfigId.Value)
                    : await context.DbSettings.FirstOrDefaultAsync();

                var validationErrors = new List<string>();

                if (config == null)
                {
                    validationErrors.Add("Configuration not found. Please create a configuration first.");
                }
                else
                {
                    if (string.IsNullOrEmpty(config.API_KEY))
                    {
                        validationErrors.Add("API_KEY is missing in configuration");
                    }

                    if (string.IsNullOrEmpty(config.API_SECRET))
                    {
                        validationErrors.Add("API_SECRET is missing in configuration");
                    }

                    if (string.IsNullOrEmpty(config.SYMBOL))
                    {
                        validationErrors.Add("SYMBOL is missing in configuration");
                    }

                    if (!config.BuyAmountUSD.HasValue || config.BuyAmountUSD.Value <= 0)
                    {
                        validationErrors.Add("BuyAmountUSD is not configured or invalid");
                    }
                }

                if (validationErrors.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Configuration validation failed",
                        status = "STOPPED",
                        errors = validationErrors,
                        configId = req.ConfigId,
                        suggestion = "Please check your configuration using GET /api/SQLite/GetById or create/update using POST /api/SQLite/CreateSetting or POST /api/SQLite/Update"
                    });
                }

                // Try to start the bot
                var result = await _botWorkerService.StartAsync(req.ConfigId);

                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Bot worker started successfully",
                        status = "RUNNING",
                        configId = config!.Id,
                        symbol = config.SYMBOL
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Failed to start bot worker. Possible causes: WebSocket connection failed, invalid API credentials, or network issues.",
                        status = "STOPPED",
                        configId = config!.Id,
                        symbol = config.SYMBOL,
                        suggestion = "Check logs for detailed error messages. Verify API credentials and network connectivity."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting bot worker");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    status = "ERROR"
                });
            }
        }

        /// <summary>
        /// Check bot worker status
        /// </summary>
        [HttpPost]
        public IActionResult CheckStatus()
        {
            try
            {
                var isRunning = _botWorkerService.IsRunning;

                return Ok(new
                {
                    success = true,
                    status = isRunning ? "RUNNING" : "STOPPED",
                    isRunning = isRunning,
                    message = isRunning ? "Bot worker is running" : "Bot worker is stopped"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking bot worker status");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    status = "ERROR"
                });
            }
        }

        /// <summary>
        /// Stop the bot worker
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Stop()
        {
            try
            {
                await _botWorkerService.StopAsync();

                // Cancel all price subscriptions
                foreach (var subscription in _subscriptions.Values)
                {
                    subscription.Cancel();
                }
                _subscriptions.Clear();

                if (_socketClient != null)
                {
                    _socketClient.Dispose();
                    _socketClient = null;
                }

                return Ok(new
                {
                    success = true,
                    message = "Bot worker stopped successfully",
                    status = "STOPPED"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping bot worker");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    status = "ERROR"
                });
            }
        }
    }
}
