using Binance.Net.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BotGridV1.Services;
using BotGridV1.Models.Binace;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class BotWorkerController : ControllerBase
    {
        private readonly BotWorkerService _botWorkerService;
        private readonly ILogger<BotWorkerController> _logger;
        private BinanceSocketClient? _socketClient;
        private Dictionary<string, System.Threading.CancellationTokenSource> _subscriptions = new();

        public BotWorkerController(BotWorkerService botWorkerService, ILogger<BotWorkerController> logger)
        {
            _botWorkerService = botWorkerService;
            _logger = logger;
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
                var result = await _botWorkerService.StartAsync(req.ConfigId);

                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Bot worker started successfully",
                        status = "RUNNING"
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Failed to start bot worker. Check configuration and API credentials.",
                        status = "STOPPED"
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
