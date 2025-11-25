using Binance.Net.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BotGridV1.Services;
using BotGridV1.Models.Binace;
using BotGridV1.Models.SQLite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using BotGridV1.Hubs;
using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class BotWorkerController : ControllerBase
    {
        private readonly BotWorkerService _botWorkerService;
        private readonly ILogger<BotWorkerController> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<OrderHub> _hubContext;
        private BinanceSocketClient? _socketClient;
        private Dictionary<string, System.Threading.CancellationTokenSource> _subscriptions = new();

        public BotWorkerController(
            BotWorkerService botWorkerService, 
            ILogger<BotWorkerController> logger, 
            IServiceProvider serviceProvider,
            IHubContext<OrderHub> hubContext)
        {
            _botWorkerService = botWorkerService;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
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

        /// <summary>
        /// Buy Now - Execute a buy order immediately at market price
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> BuyNow(req_BuyNow req)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                // Get configuration
                var config = req.ConfigId.HasValue
                    ? await context.DbSettings.FindAsync(req.ConfigId.Value)
                    : await context.DbSettings.FirstOrDefaultAsync();

                if (config == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Configuration not found"
                    });
                }

                if (string.IsNullOrEmpty(config.API_KEY) || string.IsNullOrEmpty(config.API_SECRET))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "API credentials are missing in configuration"
                    });
                }

                var symbol = req.Symbol ?? config.SYMBOL;
                if (string.IsNullOrEmpty(symbol))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Symbol is required"
                    });
                }

                var buyAmountUSD = req.BuyAmountUSD ?? config.BuyAmountUSD ?? 0;
                if (buyAmountUSD <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "BuyAmountUSD must be greater than 0"
                    });
                }

                // Create Binance REST client
                var restClient = new BinanceRestClient(options =>
                {
                    options.ApiCredentials = new ApiCredentials(config.API_KEY, config.API_SECRET);
                });

                // Get current price
                var ticker = await restClient.SpotApi.ExchangeData.GetPriceAsync(symbol);
                if (!ticker.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Failed to get current price for {symbol}: {ticker.Error?.Message}"
                    });
                }

                var currentPrice = ticker.Data.Price;

                // Check balance
                var accountInfo = await restClient.SpotApi.Account.GetAccountInfoAsync();
                if (!accountInfo.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Failed to get account info: {accountInfo.Error?.Message}"
                    });
                }

                var usdtBalance = accountInfo.Data.Balances.FirstOrDefault(b => b.Asset == "USDT");
                if (usdtBalance == null || usdtBalance.Available < buyAmountUSD)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Insufficient USDT balance. Required: {buyAmountUSD}, Available: {usdtBalance?.Available ?? 0}"
                    });
                }

                // Place market buy order
                var buyOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: OrderSide.Buy,
                    type: SpotOrderType.Market,
                    quoteQuantity: buyAmountUSD);

                if (!buyOrder.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Buy order failed: {buyOrder.Error?.Message}",
                        error = buyOrder.Error?.ToString()
                    });
                }

                var actualCoinQuantity = buyOrder.Data.QuantityFilled > 0
                    ? buyOrder.Data.QuantityFilled
                    : buyOrder.Data.Quantity;

                // Calculate sell price with PERCEN_SELL
                var sellPrice = currentPrice + (currentPrice * config.PERCEN_SELL / 100m);

                // Create order record
                var dbOrder = new DbOrder
                {
                    Timestamp = DateTime.UtcNow,
                    OrderBuyID = buyOrder.Data.Id.ToString(),
                    PriceBuy = currentPrice,
                    PriceWaitSell = sellPrice,
                    DateBuy = DateTime.UtcNow,
                    Setting_ID = config.Id,
                    Status = "WAITING_SELL",
                    Symbol = symbol,
                    Quantity = actualCoinQuantity,
                    BuyAmountUSD = buyAmountUSD,
                    CoinQuantity = actualCoinQuantity
                };

                context.DbOrders.Add(dbOrder);
                await context.SaveChangesAsync();

                // Notify SignalR clients
                await _hubContext.Clients.Group($"orders_{config.Id}").SendAsync("OrderUpdated", new
                {
                    action = "BUY_NOW",
                    order = dbOrder,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation($"Buy Now executed: Order {dbOrder.Id} at {currentPrice} for {symbol}");

                return Ok(new
                {
                    success = true,
                    message = "Buy order executed successfully",
                    order = dbOrder,
                    binanceOrderId = buyOrder.Data.Id.ToString(),
                    price = currentPrice,
                    quantity = actualCoinQuantity,
                    sellTargetPrice = sellPrice
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BuyNow");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }

        /// <summary>
        /// Sell Now - Execute a sell order immediately at market price for a specific order
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SellNow(req_SellNow req)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                // Get order from database
                var dbOrder = await context.DbOrders.FindAsync(req.OrderId);
                if (dbOrder == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"Order with ID {req.OrderId} not found"
                    });
                }

                // Validate order status
                if (dbOrder.Status != "WAITING_SELL" && dbOrder.Status != "BOUGHT")
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Order is not in a sellable state. Current status: {dbOrder.Status}",
                        currentStatus = dbOrder.Status
                    });
                }

                // Get configuration
                var config = req.ConfigId.HasValue
                    ? await context.DbSettings.FindAsync(req.ConfigId.Value)
                    : await context.DbSettings.FindAsync(dbOrder.Setting_ID);

                if (config == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Configuration not found"
                    });
                }

                if (string.IsNullOrEmpty(config.API_KEY) || string.IsNullOrEmpty(config.API_SECRET))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "API credentials are missing in configuration"
                    });
                }

                var symbol = dbOrder.Symbol ?? config.SYMBOL;
                if (string.IsNullOrEmpty(symbol))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Symbol is required"
                    });
                }

                // Get coin quantity to sell
                var coinQuantityToSell = dbOrder.CoinQuantity ?? dbOrder.Quantity ?? 0;
                if (coinQuantityToSell <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Invalid coin quantity for order {req.OrderId}. CoinQuantity: {dbOrder.CoinQuantity}, Quantity: {dbOrder.Quantity}"
                    });
                }

                // Create Binance REST client
                var restClient = new BinanceRestClient(options =>
                {
                    options.ApiCredentials = new ApiCredentials(config.API_KEY, config.API_SECRET);
                });

                // Get current price
                var ticker = await restClient.SpotApi.ExchangeData.GetPriceAsync(symbol);
                if (!ticker.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Failed to get current price for {symbol}: {ticker.Error?.Message}"
                    });
                }

                var currentPrice = ticker.Data.Price;

                // Place market sell order
                var sellOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: OrderSide.Sell,
                    type: SpotOrderType.Market,
                    quantity: coinQuantityToSell);

                if (!sellOrder.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Sell order failed: {sellOrder.Error?.Message}",
                        error = sellOrder.Error?.ToString()
                    });
                }

                // Update order in database
                dbOrder.OrderSellID = sellOrder.Data.Id.ToString();
                dbOrder.PriceSellActual = currentPrice;
                dbOrder.DateSell = DateTime.UtcNow;
                dbOrder.Status = "SOLD";

                if (dbOrder.PriceBuy.HasValue)
                {
                    dbOrder.ProfitLoss = currentPrice - dbOrder.PriceBuy.Value;
                }

                await context.SaveChangesAsync();

                // Notify SignalR clients
                await _hubContext.Clients.Group($"orders_{config.Id}").SendAsync("OrderUpdated", new
                {
                    action = "SELL_NOW",
                    order = dbOrder,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation($"Sell Now executed: Order {dbOrder.Id} at {currentPrice} for {symbol}, Profit: {dbOrder.ProfitLoss}");

                return Ok(new
                {
                    success = true,
                    message = "Sell order executed successfully",
                    order = dbOrder,
                    binanceOrderId = sellOrder.Data.Id.ToString(),
                    price = currentPrice,
                    quantity = coinQuantityToSell,
                    profitLoss = dbOrder.ProfitLoss
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SellNow");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }
    }
}
