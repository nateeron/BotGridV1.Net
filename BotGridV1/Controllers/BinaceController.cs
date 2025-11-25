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

        /// <summary>
        /// Get List of Filled Orders from Binance with comprehensive filters
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<res_GetFilledOrders>> GetFilledOrders(req_GetFilledOrders req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get configuration from database
                var config = await GetConfigAsync(req.ConfigId);
                if (config == null)
                {
                    return BadRequest(new res_GetFilledOrders
                    {
                        Success = false,
                        Message = "Configuration not found. Please provide valid ConfigId or ensure database has settings."
                    });
                }

                // Create Binance client
                var client = CreateBinanceClient(config);

                // Use symbol from request or config
                var symbol = !string.IsNullOrEmpty(req.Symbol) ? req.Symbol : config.SYMBOL;
                if (string.IsNullOrEmpty(symbol))
                {
                    return BadRequest(new res_GetFilledOrders
                    {
                        Success = false,
                        Message = "Symbol is required"
                    });
                }

                // Prepare parameters
                var limit = Math.Min(req.Limit ?? 100, 1000); // Binance max is 1000
                var startTime = req.StartTime;
                var endTime = req.EndTime;

                // Get all orders (filled and others)
                var ordersResult = await client.SpotApi.Trading.GetOrdersAsync(
                    symbol: symbol,
                    startTime: startTime,
                    endTime: endTime,
                    limit: limit);

                if (!ordersResult.Success)
                {
                    return StatusCode(500, new res_GetFilledOrders
                    {
                        Success = false,
                        Message = $"Failed to get orders: {ordersResult.Error?.Message}"
                    });
                }

                // Filter only filled orders
                var filledOrders = ordersResult.Data
                    .Where(o => o.Status == OrderStatus.Filled)
                    .AsEnumerable();

                // Filter by order side if specified
                if (!string.IsNullOrEmpty(req.OrderSide))
                {
                    var side = req.OrderSide.ToUpper() == "BUY" ? OrderSide.Buy : OrderSide.Sell;
                    filledOrders = filledOrders.Where(o => o.Side == side);
                }

                // Filter by order ID if specified
                if (req.OrderId.HasValue)
                {
                    filledOrders = filledOrders.Where(o => o.Id == req.OrderId.Value);
                }

                // Order by create time descending (newest first)
                filledOrders = filledOrders.OrderByDescending(o => o.CreateTime);

                // Convert to response model
                var ordersList = new List<res_FilledOrder>();
                foreach (var o in filledOrders)
                {
                    ordersList.Add(new res_FilledOrder
                    {
                        OrderId = o.Id,
                        Symbol = o.Symbol,
                        Side = o.Side.ToString(),
                        Type = o.Type.ToString(),
                        Quantity = o.Quantity,
                        Price = o.Price,
                        QuoteQuantity = o.QuoteQuantity,
                        QuantityFilled = o.QuantityFilled,
                        QuoteQuantityFilled = o.QuoteQuantityFilled,
                        CreateTime = o.CreateTime,
                        UpdateTime = o.UpdateTime,
                        Status = o.Status.ToString()
                    });
                }

                var totalCount = ordersList.Count;

                return Ok(new res_GetFilledOrders
                {
                    Success = true,
                    Message = "Filled orders retrieved successfully",
                    Orders = ordersList,
                    Total = totalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting filled orders");
                return StatusCode(500, new res_GetFilledOrders
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Get All Coins with Value > 0.5 USDT - Shows coin name, quantity, latest price in USDT, and sum all USD
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<res_GetAllCoins>> GetAllCoins([FromBody] req_GetSpotReport? req = null)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get configuration from database
                var config = await GetConfigAsync(req?.ConfigId);
                if (config == null)
                {
                    return BadRequest(new res_GetAllCoins
                    {
                        Success = false,
                        Message = "Configuration not found. Please provide valid ConfigId or ensure database has settings."
                    });
                }

                // Create Binance client
                var client = CreateBinanceClient(config);

                // Get account information
                var accountInfo = await client.SpotApi.Account.GetAccountInfoAsync();
                if (!accountInfo.Success)
                {
                    return StatusCode(500, new res_GetAllCoins
                    {
                        Success = false,
                        Message = $"Failed to get account info: {accountInfo.Error?.Message}"
                    });
                }

                var coins = new List<res_CoinBalance>();
                decimal totalValueUSD = 0;
                const decimal minValue = 0.5m; // Minimum value in USDT

                // Process each balance
                var balances = accountInfo.Data.Balances.Where(b => b.Total > 0);
                
                foreach (var balance in balances)
                {
                    decimal valueInUSDT = 0;
                    decimal latestPrice = 0;

                    if (balance.Asset == "USDT")
                    {
                        // USDT is already in USDT
                        valueInUSDT = balance.Total;
                        latestPrice = 1;
                    }
                    else
                    {
                        // Get current price for the asset
                        var symbol = $"{balance.Asset}USDT";
                        var ticker = await client.SpotApi.ExchangeData.GetPriceAsync(symbol);
                        
                        if (ticker.Success && ticker.Data.Price > 0)
                        {
                            latestPrice = ticker.Data.Price;
                            valueInUSDT = balance.Total * latestPrice;
                        }
                        else
                        {
                            // If USDT pair doesn't exist, try to get price through BTC
                            var btcTicker = await client.SpotApi.ExchangeData.GetPriceAsync($"{balance.Asset}BTC");
                            var usdtBtcTicker = await client.SpotApi.ExchangeData.GetPriceAsync("BTCUSDT");
                            
                            if (btcTicker.Success && usdtBtcTicker.Success)
                            {
                                latestPrice = btcTicker.Data.Price * usdtBtcTicker.Data.Price;
                                valueInUSDT = balance.Total * latestPrice;
                            }
                            else
                            {
                                // Skip if we can't get price
                                continue;
                            }
                        }
                    }

                    // Only include coins with value > 0.5 USDT
                    if (valueInUSDT > minValue)
                    {
                        coins.Add(new res_CoinBalance
                        {
                            Coin = balance.Asset,
                            Quantity = balance.Total,
                            LatestPrice = latestPrice,
                            ValueInUSDT = valueInUSDT
                        });

                        totalValueUSD += valueInUSDT;
                    }
                }

                // Sort by value descending (highest first)
                coins = coins.OrderByDescending(c => c.ValueInUSDT).ToList();

                return Ok(new res_GetAllCoins
                {
                    Success = true,
                    Message = "Coins retrieved successfully",
                    Coins = coins,
                    TotalValueUSD = totalValueUSD,
                    Count = coins.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all coins");
                return StatusCode(500, new res_GetAllCoins
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Advanced Trading API - Buy/Sell with Market/Limit orders
        /// Supports: Coin Quantity, USD Amount, or Portfolio Percentage
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<res_Trade>> Trade(req_Trade req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get configuration from database
                var config = await GetConfigAsync(req.ConfigId);
                if (config == null)
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = "Configuration not found. Please provide valid ConfigId or ensure database has settings."
                    });
                }

                // Validate symbol
                if (string.IsNullOrEmpty(req.Symbol))
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = "Symbol is required (e.g., BTCUSDT, XRPUSDT)"
                    });
                }

                // Validate side
                var side = req.Side.ToUpper();
                if (side != "BUY" && side != "SELL")
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = "Side must be 'BUY' or 'SELL'"
                    });
                }

                // Validate order type
                var orderType = req.OrderType.ToUpper();
                if (orderType != "MARKET" && orderType != "LIMIT")
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = "OrderType must be 'MARKET' or 'LIMIT'"
                    });
                }

                // Validate price for limit orders
                if (orderType == "LIMIT" && !req.Price.HasValue)
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = "Price is required for LIMIT orders"
                    });
                }

                // Validate value specification - must provide exactly one
                var valueSpecs = new[] { req.CoinQuantity.HasValue, req.UsdAmount.HasValue, req.PortfolioPercent.HasValue };
                var valueSpecCount = valueSpecs.Count(v => v);
                if (valueSpecCount != 1)
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = "Must provide exactly one: CoinQuantity, UsdAmount, or PortfolioPercent"
                    });
                }

                // Validate portfolio percent range
                if (req.PortfolioPercent.HasValue && (req.PortfolioPercent.Value <= 0 || req.PortfolioPercent.Value > 100))
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = "PortfolioPercent must be between 0 and 100"
                    });
                }

                // Create Binance client
                var client = CreateBinanceClient(config);

                // Get current price
                var ticker = await client.SpotApi.ExchangeData.GetPriceAsync(req.Symbol);
                if (!ticker.Success)
                {
                    return BadRequest(new res_Trade
                    {
                        Success = false,
                        Message = $"Failed to get current price for {req.Symbol}: {ticker.Error?.Message}"
                    });
                }
                var currentPrice = ticker.Data.Price;

                // Calculate order quantity/amount based on value specification
                decimal? orderQuantity = null;
                decimal? quoteQuantity = null;

                if (req.CoinQuantity.HasValue)
                {
                    // Direct coin quantity specified
                    orderQuantity = req.CoinQuantity.Value;
                }
                else if (req.UsdAmount.HasValue)
                {
                    // USD amount specified
                    if (side == "BUY")
                    {
                        // For buy orders, use quoteQuantity (USD amount to spend)
                        quoteQuantity = req.UsdAmount.Value;
                    }
                    else
                    {
                        // For sell orders, calculate quantity from USD amount
                        orderQuantity = req.UsdAmount.Value / currentPrice;
                    }
                }
                else if (req.PortfolioPercent.HasValue)
                {
                    // Portfolio percentage specified
                    var accountInfo = await client.SpotApi.Account.GetAccountInfoAsync();
                    if (!accountInfo.Success)
                    {
                        return StatusCode(500, new res_Trade
                        {
                            Success = false,
                            Message = $"Failed to get account info: {accountInfo.Error?.Message}"
                        });
                    }

                    // Calculate total portfolio value in USDT
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
                            var assetTicker = await client.SpotApi.ExchangeData.GetPriceAsync($"{balance.Asset}USDT");
                            if (assetTicker.Success)
                            {
                                portfolioValue += balance.Total * assetTicker.Data.Price;
                            }
                        }
                    }

                    var targetValue = portfolioValue * (req.PortfolioPercent.Value / 100m);

                    if (side == "BUY")
                    {
                        // For buy orders, use quoteQuantity (USD amount to spend)
                        quoteQuantity = targetValue;
                    }
                    else
                    {
                        // For sell orders, need to check if we have enough of the base asset
                        var baseAsset = req.Symbol.Replace("USDT", "").Replace("BTC", "").Replace("ETH", "");
                        var baseBalance = accountInfo.Data.Balances.FirstOrDefault(b => b.Asset == baseAsset);
                        
                        if (baseBalance == null || baseBalance.Available == 0)
                        {
                            return BadRequest(new res_Trade
                            {
                                Success = false,
                                Message = $"Insufficient {baseAsset} balance for sell order"
                            });
                        }

                        // Calculate quantity to sell based on target USD value
                        orderQuantity = targetValue / currentPrice;
                        
                        // Ensure we don't exceed available balance
                        if (orderQuantity > baseBalance.Available)
                        {
                            orderQuantity = baseBalance.Available;
                        }
                    }
                }

                // Validate balance before placing order
                var accountInfoCheck = await client.SpotApi.Account.GetAccountInfoAsync();
                if (!accountInfoCheck.Success)
                {
                    return StatusCode(500, new res_Trade
                    {
                        Success = false,
                        Message = $"Failed to get account info: {accountInfoCheck.Error?.Message}"
                    });
                }

                if (side == "BUY")
                {
                    var usdtBalance = accountInfoCheck.Data.Balances.FirstOrDefault(b => b.Asset == "USDT");
                    var requiredUsdt = quoteQuantity ?? (orderQuantity * currentPrice ?? 0);
                    
                    if (usdtBalance == null || usdtBalance.Available < requiredUsdt)
                    {
                        return BadRequest(new res_Trade
                        {
                            Success = false,
                            Message = $"Insufficient USDT balance. Required: {requiredUsdt:F2}, Available: {usdtBalance?.Available ?? 0:F2}"
                        });
                    }
                }
                else // SELL
                {
                    var baseAsset = req.Symbol.Replace("USDT", "").Replace("BTC", "").Replace("ETH", "");
                    var baseBalance = accountInfoCheck.Data.Balances.FirstOrDefault(b => b.Asset == baseAsset);
                    
                    if (baseBalance == null || baseBalance.Available < (orderQuantity ?? 0))
                    {
                        return BadRequest(new res_Trade
                        {
                            Success = false,
                            Message = $"Insufficient {baseAsset} balance. Required: {orderQuantity:F8}, Available: {baseBalance?.Available ?? 0:F8}"
                        });
                    }
                }

                // Place order
                WebCallResult<Binance.Net.Objects.Models.Spot.BinancePlacedOrder>? orderResult = null;

                if (orderType == "MARKET")
                {
                    if (side == "BUY")
                    {
                        if (quoteQuantity.HasValue)
                        {
                            // Market buy with quote quantity (USD amount)
                            orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
                                symbol: req.Symbol,
                                side: OrderSide.Buy,
                                type: SpotOrderType.Market,
                                quoteQuantity: quoteQuantity.Value);
                        }
                        else if (orderQuantity.HasValue)
                        {
                            // Market buy with base quantity
                            orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
                                symbol: req.Symbol,
                                side: OrderSide.Buy,
                                type: SpotOrderType.Market,
                                quantity: orderQuantity.Value);
                        }
                    }
                    else // SELL
                    {
                        orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
                            symbol: req.Symbol,
                            side: OrderSide.Sell,
                            type: SpotOrderType.Market,
                            quantity: orderQuantity!.Value);
                    }
                }
                else // LIMIT
                {
                    if (!orderQuantity.HasValue)
                    {
                        return BadRequest(new res_Trade
                        {
                            Success = false,
                            Message = "Order quantity could not be calculated"
                        });
                    }

                    var timeInForce = req.TimeInForce.HasValue 
                        ? req.TimeInForce.Value 
                        : Binance.Net.Enums.TimeInForce.GoodTillCanceled;
                    
                    orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
                        symbol: req.Symbol,
                        side: side == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                        type: SpotOrderType.Limit,
                        quantity: orderQuantity.Value,
                        price: req.Price!.Value,
                        timeInForce: timeInForce);
                }

                if (!orderResult.Success || orderResult.Data == null)
                {
                    return StatusCode(500, new res_Trade
                    {
                        Success = false,
                        Message = $"Order failed: {orderResult.Error?.Message}",
                        Symbol = req.Symbol,
                        Side = side,
                        OrderType = orderType
                    });
                }

                var order = orderResult.Data;
                decimal actualPrice;
                if (order.QuantityFilled > 0 && order.QuoteQuantityFilled > 0)
                {
                    actualPrice = order.QuoteQuantityFilled / order.QuantityFilled;
                }
                else if (order.Price > 0)
                {
                    actualPrice = order.Price;
                }
                else
                {
                    actualPrice = currentPrice;
                }

                return Ok(new res_Trade
                {
                    Success = true,
                    Message = $"{side} {orderType} order placed successfully",
                    OrderId = order.Id,
                    Symbol = req.Symbol,
                    Side = side,
                    OrderType = orderType,
                    Status = order.Status.ToString(),
                    Quantity = order.Quantity,
                    Price = order.Price,
                    QuantityFilled = order.QuantityFilled,
                    QuoteQuantityFilled = order.QuoteQuantityFilled,
                    ActualPrice = actualPrice,
                    TotalCost = order.QuoteQuantityFilled > 0 ? order.QuoteQuantityFilled : (order.Quantity * actualPrice),
                    CreateTime = order.CreateTime,
                    AdditionalInfo = new Dictionary<string, object>
                    {
                        { "RequestedValue", req.CoinQuantity ?? req.UsdAmount ?? req.PortfolioPercent ?? 0 },
                        { "ValueType", req.CoinQuantity.HasValue ? "CoinQuantity" : req.UsdAmount.HasValue ? "UsdAmount" : "PortfolioPercent" },
                        { "CurrentPrice", currentPrice }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing trade order");
                return StatusCode(500, new res_Trade
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Get Binance Server Time and compare with local time
        /// Helps diagnose "Timestamp for this request is outside of the recvWindow" errors
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<res_ServerTime>> GetServerTime(req_GetServerTime? req = null)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get configuration from database
                var config = await GetConfigAsync(req?.ConfigId);
                if (config == null)
                {
                    return BadRequest(new res_ServerTime
                    {
                        Success = false,
                        Message = "Configuration not found. Please provide valid ConfigId or ensure database has settings."
                    });
                }

                // Create Binance client
                var client = CreateBinanceClient(config);

                // Get server time from Binance
                var serverTimeResult = await client.SpotApi.ExchangeData.GetServerTimeAsync();
                
                if (!serverTimeResult.Success)
                {
                    return StatusCode(500, new res_ServerTime
                    {
                        Success = false,
                        Message = $"Failed to get server time: {serverTimeResult.Error?.Message}"
                    });
                }

                var serverTime = serverTimeResult.Data;
                var localTime = DateTime.UtcNow;
                var timeDifference = serverTime - localTime;
                var timeDifferenceMs = (long)timeDifference.TotalMilliseconds;
                
                // Check if synchronized (within ±1000ms is acceptable)
                var isSynchronized = Math.Abs(timeDifferenceMs) <= 1000;
                
                string? recommendation = null;
                if (!isSynchronized)
                {
                    if (timeDifferenceMs > 1000)
                    {
                        recommendation = $"Server time is {Math.Abs(timeDifferenceMs)}ms ahead of local time. Consider syncing system clock or using NTP.";
                    }
                    else if (timeDifferenceMs < -1000)
                    {
                        recommendation = $"Server time is {Math.Abs(timeDifferenceMs)}ms behind local time. Consider syncing system clock or using NTP.";
                    }
                }
                else
                {
                    recommendation = "Time is synchronized. No action needed.";
                }

                return Ok(new res_ServerTime
                {
                    Success = true,
                    Message = "Server time retrieved successfully",
                    ServerTime = serverTime,
                    LocalTime = localTime,
                    TimeDifferenceMs = timeDifferenceMs,
                    IsSynchronized = isSynchronized,
                    Recommendation = recommendation
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting server time");
                return StatusCode(500, new res_ServerTime
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
