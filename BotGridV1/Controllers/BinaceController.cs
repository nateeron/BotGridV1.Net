using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BotGridV1.Models.Binace;
using BotGridV1.Models.SQLite;
using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class BinaceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BinaceController> _logger;

        public BinaceController(ApplicationDbContext context, ILogger<BinaceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get Binance Spot Report - Portfolio value, Order wait, Order success for different time periods
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<res_SpotReport>> GetSpotReport(req_GetSpotReport req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get configuration from database
                var config = await GetConfigAsync(req.ConfigId);
                if (config == null)
                {
                    return BadRequest(new res_SpotReport
                    {
                        Success = false,
                        Message = "Configuration not found. Please provide valid ConfigId or ensure database has settings."
                    });
                }

                // Create Binance client
                var client = CreateBinanceClient(config);

                // Parse period
                var period = req.Period ?? "1M";
                var startTime = GetStartTimeForPeriod(period);

                // Get account information (portfolio value)
                var accountInfo = await client.SpotApi.Account.GetAccountInfoAsync();
                if (!accountInfo.Success)
                {
                    return StatusCode(500, new res_SpotReport
                    {
                        Success = false,
                        Message = $"Failed to get account info: {accountInfo.Error?.Message}"
                    });
                }

                // Calculate portfolio value in USDT
                decimal portfolioValue = 0;
                var balances = accountInfo.Data.Balances.Where(b => b.Total > 0);
                foreach (var balance in balances)
                {
                    if (balance.Asset == "USDT")
                    {
                        portfolioValue += balance.Total;
                    }
                    else
                    {
                        // Get current price for the asset
                        var ticker = await client.SpotApi.ExchangeData.GetPriceAsync($"{balance.Asset}USDT");
                        if (ticker.Success)
                        {
                            portfolioValue += balance.Total * ticker.Data.Price;
                        }
                    }
                }

                // Get orders for the specified period
                var symbol = config.SYMBOL ?? "BTCUSDT";
                var ordersResult = await client.SpotApi.Trading.GetOrdersAsync(symbol: symbol, startTime: startTime);
                if (!ordersResult.Success)
                {
                    return StatusCode(500, new res_SpotReport
                    {
                        Success = false,
                        Message = $"Failed to get orders: {ordersResult.Error?.Message}"
                    });
                }

                var orders = ordersResult.Data;
                var ordersWaiting = orders.Count(o => o.Status == OrderStatus.New || o.Status == OrderStatus.PartiallyFilled);
                var ordersSuccess = orders.Count(o => o.Status == OrderStatus.Filled);

                return Ok(new res_SpotReport
                {
                    Success = true,
                    Message = "Report generated successfully",
                    PortfolioValue = portfolioValue,
                    OrdersWaiting = ordersWaiting,
                    OrdersSuccess = ordersSuccess,
                    Period = period,
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "TotalOrders", orders.Length },
                        { "Symbol", symbol }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting spot report");
                return StatusCode(500, new res_SpotReport
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Open Order - Buy/Sell Spot and Buy with Limit Sell
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<res_OpenOrder>> OpenOrder(req_OpenOrder req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get configuration from database
                var config = await GetConfigAsync(req.ConfigId);
                if (config == null)
                {
                    return BadRequest(new res_OpenOrder
                    {
                        Success = false,
                        Message = "Configuration not found. Please provide valid ConfigId or ensure database has settings."
                    });
                }

                // Use symbol from request or config
                var symbol = !string.IsNullOrEmpty(req.Symbol) ? req.Symbol : config.SYMBOL;
                if (string.IsNullOrEmpty(symbol))
                {
                    return BadRequest(new res_OpenOrder
                    {
                        Success = false,
                        Message = "Symbol is required"
                    });
                }

                // Create Binance client
                var client = CreateBinanceClient(config);

                WebCallResult<Binance.Net.Objects.Models.Spot.BinancePlacedOrder>? orderResult = null;

                switch (req.OrderType.ToUpper())
                {
                    case "BUY":
                        // Market buy order
                        if (!req.Quantity.HasValue)
                        {
                            return BadRequest(new res_OpenOrder
                            {
                                Success = false,
                                Message = "Quantity is required for BUY order"
                            });
                        }
                        orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
                            symbol: symbol,
                            side: OrderSide.Buy,
                            type: SpotOrderType.Market,
                            quantity: req.Quantity.Value);
                        break;

                    case "SELL":
                        // Market sell order
                        if (!req.Quantity.HasValue)
                        {
                            return BadRequest(new res_OpenOrder
                            {
                                Success = false,
                                Message = "Quantity is required for SELL order"
                            });
                        }
                        orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
                            symbol: symbol,
                            side: OrderSide.Sell,
                            type: SpotOrderType.Market,
                            quantity: req.Quantity.Value);
                        break;

                    case "BUY_LIMIT_SELL":
                        // Buy market order, then place limit sell order
                        if (!req.Quantity.HasValue)
                        {
                            return BadRequest(new res_OpenOrder
                            {
                                Success = false,
                                Message = "Quantity is required for BUY_LIMIT_SELL order"
                            });
                        }
                        if (!req.LimitPrice.HasValue)
                        {
                            return BadRequest(new res_OpenOrder
                            {
                                Success = false,
                                Message = "LimitPrice is required for BUY_LIMIT_SELL order"
                            });
                        }

                        // First, place market buy order
                        var buyResult = await client.SpotApi.Trading.PlaceOrderAsync(
                            symbol: symbol,
                            side: OrderSide.Buy,
                            type: SpotOrderType.Market,
                            quantity: req.Quantity.Value);

                        if (!buyResult.Success)
                        {
                            return StatusCode(500, new res_OpenOrder
                            {
                                Success = false,
                                Message = $"Buy order failed: {buyResult.Error?.Message}",
                                OrderType = "BUY_LIMIT_SELL"
                            });
                        }

                        // Wait a bit for the buy order to fill
                        await Task.Delay(1000);

                        // Then place limit sell order
                        orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
                            symbol: symbol,
                            side: OrderSide.Sell,
                            type: SpotOrderType.Limit,
                            quantity: req.Quantity.Value,
                            price: req.LimitPrice.Value,
                            timeInForce: TimeInForce.GoodTillCanceled);

                        if (!orderResult.Success)
                        {
                            return StatusCode(500, new res_OpenOrder
                            {
                                Success = false,
                                Message = $"Buy order succeeded but limit sell order failed: {orderResult.Error?.Message}",
                                OrderId = buyResult.Data.Id,
                                OrderType = "BUY_LIMIT_SELL"
                            });
                        }

                        return Ok(new res_OpenOrder
                        {
                            Success = true,
                            Message = "Buy order and limit sell order placed successfully",
                            OrderId = orderResult.Data.Id,
                            Symbol = symbol,
                            OrderType = "BUY_LIMIT_SELL",
                            Status = orderResult.Data.Status.ToString(),
                            Quantity = orderResult.Data.Quantity,
                            Price = orderResult.Data.Price
                        });

                    default:
                        return BadRequest(new res_OpenOrder
                        {
                            Success = false,
                            Message = $"Invalid OrderType. Use: BUY, SELL, or BUY_LIMIT_SELL"
                        });
                }

                if (!orderResult.Success)
                {
                    return StatusCode(500, new res_OpenOrder
                    {
                        Success = false,
                        Message = $"Order failed: {orderResult.Error?.Message}",
                        OrderType = req.OrderType
                    });
                }

                return Ok(new res_OpenOrder
                {
                    Success = true,
                    Message = "Order placed successfully",
                    OrderId = orderResult.Data.Id,
                    Symbol = symbol,
                    OrderType = req.OrderType,
                    Status = orderResult.Data.Status.ToString(),
                    Quantity = orderResult.Data.Quantity,
                    Price = orderResult.Data.Price
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening order");
                return StatusCode(500, new res_OpenOrder
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Cancel Order - Cancel buy or sell order
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<res_CancelOrder>> CancelOrder(req_CancelOrder req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get configuration from database
                var config = await GetConfigAsync(req.ConfigId);
                if (config == null)
                {
                    return BadRequest(new res_CancelOrder
                    {
                        Success = false,
                        Message = "Configuration not found. Please provide valid ConfigId or ensure database has settings."
                    });
                }

                // Use symbol from request or config
                var symbol = !string.IsNullOrEmpty(req.Symbol) ? req.Symbol : config.SYMBOL;
                if (string.IsNullOrEmpty(symbol))
                {
                    return BadRequest(new res_CancelOrder
                    {
                        Success = false,
                        Message = "Symbol is required"
                    });
                }

                if (!req.OrderId.HasValue)
                {
                    return BadRequest(new res_CancelOrder
                    {
                        Success = false,
                        Message = "OrderId is required"
                    });
                }

                // Create Binance client
                var client = CreateBinanceClient(config);

                // Cancel the order
                var cancelResult = await client.SpotApi.Trading.CancelOrderAsync(
                    symbol: symbol,
                    orderId: req.OrderId.Value);

                if (!cancelResult.Success)
                {
                    return StatusCode(500, new res_CancelOrder
                    {
                        Success = false,
                        Message = $"Failed to cancel order: {cancelResult.Error?.Message}",
                        OrderId = req.OrderId.Value,
                        Symbol = symbol
                    });
                }

                return Ok(new res_CancelOrder
                {
                    Success = true,
                    Message = "Order cancelled successfully",
                    OrderId = req.OrderId.Value,
                    Symbol = symbol
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error canceling order");
                return StatusCode(500, new res_CancelOrder
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        #region Helper Methods

        private async Task<DbSetting?> GetConfigAsync(int? configId)
        {
            if (configId.HasValue)
            {
                return await _context.DbSettings.FindAsync(configId.Value);
            }
            else
            {
                // Get the first available config
                return await _context.DbSettings.FirstOrDefaultAsync();
            }
        }

        private BinanceRestClient CreateBinanceClient(DbSetting config)
        {
            if (string.IsNullOrEmpty(config.API_KEY) || string.IsNullOrEmpty(config.API_SECRET))
            {
                throw new Exception("API_KEY and API_SECRET must be configured in database");
            }

            return new BinanceRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(config.API_KEY, config.API_SECRET);
            });
        }

        private DateTime? GetStartTimeForPeriod(string period)
        {
            var now = DateTime.UtcNow;
            return period.ToUpper() switch
            {
                "1M" => now.AddMonths(-1),
                "2M" => now.AddMonths(-2),
                "3M" => now.AddMonths(-3),
                "1Y" => now.AddYears(-1),
                "2Y" => now.AddYears(-2),
                "3Y" => now.AddYears(-3),
                _ => now.AddMonths(-1) // Default to 1 month
            };
        }

        #endregion
    }
}
