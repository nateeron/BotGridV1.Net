using Binance.Net.Clients;
using Binance.Net.Enums;
using BotGridV1.Models.SQLite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using BotGridV1.Models.SQLite;
using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace BotGridV1.Services
{
    public class BotWorkerV2Controller : ControllerBase
    {
        /// <summary>
        /// BotWorkerService V2
        /// - แยก lock ซื้อ/ขาย (_buyLock, _sellLock)
        /// - ลดโอกาส race condition ซื้อ/ขายซ้ำ
        /// - รวม logic เช็กเงื่อนไขซื้อไว้ในที่เดียว
        /// - Refresh cache จาก DbOrders ในจุดเดียว
        /// </summary>

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BotWorkerV2Controller> _logger;
        private readonly DiscordService? _discordService;

        private BinanceSocketClient? _socketClient;
        private CancellationTokenSource? _cancellationTokenSource;

        private readonly object _stateLock = new();   // สำหรับ _orderCache, _currentConfig
        private readonly object _buyLock = new();     // lock logic ซื้อ
        private readonly object _sellLock = new();    // lock logic ขาย

        private bool _isRunning = false;

        private List<OrderCache> _orderCache = new(); // WAITING_SELL cache
        private DateTime _lastBuyTime = DateTime.MinValue;
        private DateTime _lastSellTime = DateTime.MinValue;

        private readonly TimeSpan _minBuyInterval = TimeSpan.FromSeconds(2);  // กันซื้อซ้ำถี่ๆ
        private readonly TimeSpan _minSellInterval = TimeSpan.FromSeconds(1); // กันขายซ้ำถี่ๆ
        private DateTime? _waitBuyTime = null;         // เวลาที่เริ่มรอซื้อเมื่อไม่มี openSellOrders

        private DbSetting? _currentConfig;

        public BotWorkerV2Controller(
            IServiceProvider serviceProvider,
            ILogger<BotWorkerV2Controller> logger,
            DiscordService? discordService = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _discordService = discordService;
        }

        #region Start / Stop API

        public bool IsRunning => _isRunning;

        public async Task<bool> StartAsync(int? configId = null)
        {
            if (_isRunning)
            {
                _logger.LogWarning("Bot worker is already running");
                return false;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var config = configId.HasValue
                    ? await context.DbSettings.FindAsync(configId.Value)
                    : await context.DbSettings.FirstOrDefaultAsync();

                if (config == null || string.IsNullOrEmpty(config.API_KEY) || string.IsNullOrEmpty(config.API_SECRET))
                {
                    if (_discordService != null && config != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            "Configuration not found or API credentials missing",
                            "Bot start failed");
                    }
                    return false;
                }

                if (string.IsNullOrEmpty(config.SYMBOL))
                {
                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            "Symbol not configured",
                            "Bot start failed");
                    }
                    return false;
                }

                lock (_stateLock)
                {
                    _currentConfig = config;
                    _orderCache.Clear();
                    _waitBuyTime = null;
                    _lastBuyTime = DateTime.MinValue;
                    _lastSellTime = DateTime.MinValue;
                }

                // โหลด cache เริ่มต้น
                await ReloadOrderCacheFromDbAsync(context, config.Id);

                _socketClient = new BinanceSocketClient();
                _cancellationTokenSource = new CancellationTokenSource();

                var symbol = config.SYMBOL;
                var subscription = await _socketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(
                    symbol,
                    async data => { await ProcessPriceUpdateAsync(data.Data.Price, config); },
                    _cancellationTokenSource.Token);

                if (!subscription.Success)
                {
                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Failed to subscribe to {symbol}",
                            subscription.Error?.Message ?? "Unknown error");
                    }
                    return false;
                }

                _isRunning = true;
                _logger.LogInformation("Bot worker started for symbol {Symbol}", symbol);

                if (_discordService != null)
                {
                    await _discordService.LogStartAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        symbol,
                        config.Id);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (_discordService != null && _currentConfig != null)
                {
                    await _discordService.LogErrorAsync(
                        _currentConfig.DisCord_Hook1,
                        _currentConfig.DisCord_Hook2,
                        "Error starting bot worker",
                        ex.Message);
                }
                return false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            try
            {
                string? symbol = null;
                DbSetting? configSnapshot = null;

                lock (_stateLock)
                {
                    symbol = _currentConfig?.SYMBOL;
                    configSnapshot = _currentConfig;
                    _orderCache.Clear();
                    _waitBuyTime = null;
                    _isRunning = false;
                }

                _cancellationTokenSource?.Cancel();
                _socketClient?.Dispose();
                _socketClient = null;

                _logger.LogInformation("Bot worker stopped");

                if (_discordService != null && configSnapshot != null)
                {
                    await _discordService.LogStopAsync(
                        configSnapshot.DisCord_Hook1,
                        configSnapshot.DisCord_Hook2,
                        symbol);
                }

                lock (_stateLock)
                {
                    _currentConfig = null;
                }
            }
            catch (Exception ex)
            {
                if (_discordService != null && _currentConfig != null)
                {
                    await _discordService.LogErrorAsync(
                        _currentConfig.DisCord_Hook1,
                        _currentConfig.DisCord_Hook2,
                        "Error stopping bot worker",
                        ex.Message);
                }
            }
        }

        #endregion

        #region Price Handler

        /// <summary>
        /// ถูกเรียกทุกครั้งที่มี trade update จาก Binance
        /// </summary>
        private async Task ProcessPriceUpdateAsync(decimal currentPrice, DbSetting config)
        {
            try
            {
                // เช็กเร็วๆ กันซื้อ/ขายถี่เกินโดยไม่ต้องแตะ DB ถ้ายังไม่ถึง interval เลย
                var now = DateTime.UtcNow;
                if (_lastBuyTime != DateTime.MinValue &&
                    now - _lastBuyTime < _minBuyInterval &&
                    _lastSellTime != DateTime.MinValue &&
                    now - _lastSellTime < _minSellInterval)
                {
                    // ทั้งซื้อและขายเพิ่งทำงานไปไม่นาน -> ข้ามรอบนี้เลย
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // ดึง order รอขายจาก DB (top 20 ราคาต่ำสุด)
                var waitingSellOrders = await context.DbOrders
                    .Where(o => o.Setting_ID == config.Id &&
                                o.Status == "WAITING_SELL" &&
                                o.PriceWaitSell.HasValue)
                    .ToListAsync();

                waitingSellOrders = waitingSellOrders
                    .OrderBy(o => o.PriceWaitSell!.Value)
                    .Take(20)
                    .ToList();

                // อัปเดต cache
                var cacheSnapshot = waitingSellOrders
                    .Select(o => new OrderCache
                    {
                        Id = o.Id,
                        OrderBuyID = o.OrderBuyID,
                        PriceBuy = o.PriceBuy,
                        PriceWaitSell = o.PriceWaitSell!.Value,
                        Setting_ID = o.Setting_ID,
                        Status = o.Status,
                        Symbol = o.Symbol
                    })
                    .ToList();

                lock (_stateLock)
                {
                    _orderCache = cacheSnapshot;
                }

                // เช็กเงื่อนไข "ซื้อ"
                await CheckAndBuyAsync(context, config, currentPrice, waitingSellOrders);

                // เช็กเงื่อนไข "ขาย"
                var sellOrdersCache = cacheSnapshot; // ใช้ snapshot เดียวกันกับด้านบน
                await CheckAndSellAsync(context, config, currentPrice, sellOrdersCache);
            }
            catch (Exception ex)
            {
                if (_discordService != null && config != null)
                {
                    await _discordService.LogErrorAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        $"Error processing price update for {config.SYMBOL}",
                        ex.Message);
                }
            }
        }

        #endregion

        #region BUY Logic

        /// <summary>
        /// ตรวจสอบและส่งคำสั่งซื้อ ถ้าเข้าเงื่อนไข
        /// </summary>
        private async Task CheckAndBuyAsync(ApplicationDbContext context,DbSetting config,decimal currentPrice,List<DbOrder> waitingSellOrders)
        {
            try
            {
                // 1) โหลด action ล่าสุดจาก DB
                var lastActionOrder = await context.DbOrders
                    .Where(o => o.Setting_ID == config.Id)
                    .OrderByDescending(o => o.Id)
                    .FirstOrDefaultAsync();

                // 2) คำนวณ threshold และตัดสินใจว่าควรซื้อหรือไม่
                bool shouldBuy = false;
                decimal? thresholdDown = null;
                decimal? runUpThreshold = null;
                var openSellCount = waitingSellOrders.Count;

                if (lastActionOrder == null)
                {
                    // ไม่มี order เลย -> ซื้อได้ทันที
                    shouldBuy = true;
                    _logger.LogInformation(
                        "No orders found for Config {ConfigId}. Will buy immediately at price {Price}",
                        config.Id, currentPrice);
                }
                else if (lastActionOrder.Status == "SOLD" && lastActionOrder.PriceSellActual.HasValue)
                {
                    var lastPrice = lastActionOrder.PriceSellActual.Value;
                    thresholdDown = lastPrice - (lastPrice * config.PERCEN_BUY / 100m);
                    runUpThreshold = lastPrice + (lastPrice * config.PERCEN_BUY / 100m);

                    // จัดการ wait-time logic เมื่อไม่มี openSellOrders
                    if (openSellCount == 0 && runUpThreshold.HasValue)
                    {
                        if (currentPrice < runUpThreshold.Value && !_waitBuyTime.HasValue)
                        {
                            // เริ่มจับเวลารอซื้อ
                            _waitBuyTime = DateTime.UtcNow;
                        }
                        else if (currentPrice >= runUpThreshold.Value)
                        {
                            // ถ้าราคากลับขึ้นไป run-up แล้ว และยังไม่มี order -> ซื้อได้เลย
                            shouldBuy = true;
                            _waitBuyTime = null;
                        }

                        // ถ้าราคายังต่ำกว่า runUp แต่รอเกิน 5 นาทีแล้ว และยังไม่มี openSell
                        if (!shouldBuy && _waitBuyTime.HasValue &&
                            DateTime.UtcNow - _waitBuyTime.Value >= TimeSpan.FromMinutes(5) &&
                            currentPrice < runUpThreshold.Value)
                        {
                            shouldBuy = true;
                            _waitBuyTime = null;
                        }
                    }

                    // เงื่อนไขราคาลงต่ำกว่าจุดที่กำหนด
                    if (!shouldBuy && thresholdDown.HasValue && currentPrice <= thresholdDown.Value)
                    {
                        shouldBuy = true;
                    }
                }
                else if (!string.IsNullOrEmpty(lastActionOrder.OrderBuyID) &&
                         lastActionOrder.PriceBuy.HasValue &&
                         lastActionOrder.Status != "WAITING_SELL")
                {
                    // กรณีล่าสุดเป็น Buy แต่ยังไม่ได้ขาย ใช้ dip จากราคา buy
                    var basePrice = lastActionOrder.PriceBuy.Value;
                    thresholdDown = basePrice - (basePrice * config.PERCEN_BUY / 100m);

                    if (currentPrice <= thresholdDown)
                    {
                        shouldBuy = true;
                    }
                }
                else if (lastActionOrder.Status == "WAITING_SELL")
                {
                    // มี order รอขายอยู่ -> ไม่ซื้อเพิ่ม
                    _logger.LogDebug(
                        "Skip buy: last action is WAITING_SELL (Order {OrderId})",
                        lastActionOrder.Id);
                    shouldBuy = false;
                }

                if (!shouldBuy)
                    return;

                // 3) ใช้ lock กันซื้อซ้ำ และเช็ก interval อีกครั้งแบบ atomic
                lock (_buyLock)
                {
                    var now = DateTime.UtcNow;
                    if (_lastBuyTime != DateTime.MinValue &&
                        now - _lastBuyTime < _minBuyInterval)
                    {
                        _logger.LogDebug(
                            "Buy skipped by interval guard. Elapsed {Elapsed}",
                            now - _lastBuyTime);
                        return;
                    }

                    _lastBuyTime = now; // จอง slot ซื้อรอบนี้
                }

                // 4) ตรวจสอบ config.BuyAmountUSD
                if (!config.BuyAmountUSD.HasValue || config.BuyAmountUSD.Value <= 0)
                {
                    _logger.LogWarning("BuyAmountUSD is not configured or invalid");
                    return;
                }

                var restClient = CreateBinanceClient(config);
                var symbol = config.SYMBOL!;
                var buyAmountUSD = config.BuyAmountUSD.Value;

                // 5) ตรวจสอบ balance
                var accountInfo = await restClient.SpotApi.Account.GetAccountInfoAsync();
                if (!accountInfo.Success)
                {
                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Failed to get account info for {symbol}",
                            accountInfo.Error?.Message ?? "Unknown error");
                    }
                    return;
                }

                var usdtBalance = accountInfo.Data.Balances.FirstOrDefault(b => b.Asset == "USDT");
                if (usdtBalance == null || usdtBalance.Available < buyAmountUSD)
                {
                    _logger.LogWarning(
                        "Insufficient USDT balance. Required {Req}, Available {Avail}",
                        buyAmountUSD, usdtBalance?.Available ?? 0);

                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Insufficient USDT balance - Stopping bot for {symbol}",
                            $"Required: {buyAmountUSD}, Available: {usdtBalance?.Available ?? 0}");
                    }
                    return;
                }

                // 6) วางคำสั่งซื้อ (market)
                var buyOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: OrderSide.Buy,
                    type: SpotOrderType.Market,
                    quoteQuantity: buyAmountUSD);

                if (!buyOrder.Success)
                {
                    // log fail + retry 1 ครั้ง
                    if (_discordService != null)
                    {
                        await _discordService.LogBuyNotSuccessAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            symbol,
                            buyOrder.Error?.Message ?? "Unknown error",
                            0);
                    }

                    await Task.Delay(1000);

                    if (_discordService != null)
                    {
                        await _discordService.LogBuyRetryAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            symbol,
                            currentPrice,
                            1,
                            buyOrder.Error?.Message ?? "Retrying after failure");
                    }

                    var retryBuyOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                        symbol: symbol,
                        side: OrderSide.Buy,
                        type: SpotOrderType.Market,
                        quoteQuantity: buyAmountUSD);

                    if (!retryBuyOrder.Success)
                    {
                        if (_discordService != null)
                        {
                            await _discordService.LogBuyNotSuccessAsync(
                                config.DisCord_Hook1,
                                config.DisCord_Hook2,
                                symbol,
                                retryBuyOrder.Error?.Message ?? "Retry failed",
                                1);
                        }
                        return;
                    }

                    buyOrder = retryBuyOrder;
                }

                var actualCoinQuantity = buyOrder.Data.QuantityFilled > 0
                    ? buyOrder.Data.QuantityFilled
                    : buyOrder.Data.Quantity;

                var sellPrice = currentPrice + (currentPrice * config.PERCEN_SELL / 100m);

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

                // reset เวลารอซื้อเมื่อซื้อสำเร็จ
                _waitBuyTime = null;

                // อัปเดต cache ใน memory
                lock (_stateLock)
                {
                    _orderCache.Add(new OrderCache
                    {
                        Id = dbOrder.Id,
                        OrderBuyID = dbOrder.OrderBuyID,
                        PriceBuy = dbOrder.PriceBuy,
                        PriceWaitSell = dbOrder.PriceWaitSell ?? 0,
                        Setting_ID = dbOrder.Setting_ID,
                        Status = dbOrder.Status,
                        Symbol = dbOrder.Symbol
                    });
                }

                _logger.LogInformation(
                    "Buy order placed: {OrderId} at {Price}, Sell target: {SellPrice}",
                    buyOrder.Data.Id, currentPrice, sellPrice);

                if (_discordService != null)
                {
                    await _discordService.LogBuyAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        symbol,
                        currentPrice,
                        actualCoinQuantity,
                        buyAmountUSD,
                        buyOrder.Data.Id.ToString());
                }
            }
            catch (Exception ex)
            {
                if (_discordService != null && config != null)
                {
                    await _discordService.LogErrorAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        $"Error in CheckAndBuyAsync for {config.SYMBOL}",
                        ex.Message);
                }
            }
        }

        #endregion

        #region SELL Logic

        private async Task CheckAndSellAsync(ApplicationDbContext context,DbSetting config,decimal currentPrice,List<OrderCache> waitingSellOrders)
        {
            try
            {
                foreach (var orderCache in waitingSellOrders)
                {
                    // ยังไม่ถึงราคาเป้าขาย
                    if (currentPrice < orderCache.PriceWaitSell)
                        continue;

                    bool shouldProcessSell = false;
                    var now = DateTime.UtcNow;

                    // ใช้ sell lock กันขายซ้ำ และควบคุม interval
                    lock (_sellLock)
                    {
                        if (_lastSellTime != DateTime.MinValue &&
                            now - _lastSellTime < _minSellInterval)
                        {
                            // เพิ่งขายไปเมื่อเร็วๆ นี้ ไม่ขายซ้ำรอบเดียวกัน
                            return;
                        }

                        // ตรวจสอบซ้ำใน cache ว่ายัง WAITING_SELL อยู่
                        var cachedOrder = _orderCache.FirstOrDefault(o => o.Id == orderCache.Id);
                        if (cachedOrder != null && cachedOrder.Status == "WAITING_SELL")
                        {
                            shouldProcessSell = true;
                            _lastSellTime = now;
                            // mark ชั่วคราวว่าไม่ควรขายซ้ำ
                            cachedOrder.Status = "SELLING";
                        }
                    }

                    if (!shouldProcessSell)
                        continue;

                    // ดึงข้อมูล order จาก DB (ข้อมูลล่าสุด)
                    var dbOrder = await context.DbOrders.FindAsync(orderCache.Id);
                    if (dbOrder == null)
                    {
                        // ไม่มีใน DB แล้ว -> ลบออกจาก cache
                        lock (_stateLock)
                        {
                            _orderCache.RemoveAll(o => o.Id == orderCache.Id);
                        }
                        continue;
                    }

                    if (dbOrder.Status != "WAITING_SELL")
                    {
                        // มี thread อื่นขายไปแล้ว -> ลบจาก cache
                        lock (_stateLock)
                        {
                            _orderCache.RemoveAll(o => o.Id == orderCache.Id);
                        }
                        continue;
                    }

                    var restClient = CreateBinanceClient(config);
                    var symbol = dbOrder.Symbol ?? config.SYMBOL!;
                    var coinQuantityToSell = dbOrder.CoinQuantity ?? dbOrder.Quantity ?? 0;

                    if (coinQuantityToSell <= 0)
                    {
                        if (_discordService != null)
                        {
                            await _discordService.LogErrorAsync(
                                config.DisCord_Hook1,
                                config.DisCord_Hook2,
                                $"Invalid coin quantity for order {dbOrder.Id}",
                                $"CoinQuantity: {dbOrder.CoinQuantity}, Quantity: {dbOrder.Quantity}");
                        }
                        continue;
                    }

                    var sellOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                        symbol: symbol,
                        side: OrderSide.Sell,
                        type: SpotOrderType.Market,
                        quantity: coinQuantityToSell);

                    if (!sellOrder.Success)
                    {
                        if (_discordService != null)
                        {
                            await _discordService.LogErrorAsync(
                                config.DisCord_Hook1,
                                config.DisCord_Hook2,
                                $"Sell order failed for {symbol}",
                                sellOrder.Error?.Message ?? "Unknown error");
                        }

                        // ถ้าขายไม่สำเร็จ ให้ revert status ใน cache กลับเป็น WAITING_SELL
                        lock (_stateLock)
                        {
                            var cached = _orderCache.FirstOrDefault(o => o.Id == orderCache.Id);
                            if (cached != null && cached.Status == "SELLING")
                                cached.Status = "WAITING_SELL";
                        }

                        continue;
                    }

                    // ตรวจสอบอีกครั้งว่า order ยัง WAITING_SELL ก่อนอัปเดต
                    var freshDbOrder = await context.DbOrders.FindAsync(orderCache.Id);
                    if (freshDbOrder == null || freshDbOrder.Status != "WAITING_SELL")
                    {
                        if (_discordService != null)
                        {
                            await _discordService.LogErrorAsync(
                                config.DisCord_Hook1,
                                config.DisCord_Hook2,
                                $"Sell order conflict for {symbol}",
                                $"Order {orderCache.Id} already sold. Binance Order ID: {sellOrder.Data.Id}");
                        }
                        continue;
                    }

                    try
                    {
                        freshDbOrder.OrderSellID = sellOrder.Data.Id.ToString();
                        freshDbOrder.PriceSellActual = currentPrice;
                        freshDbOrder.DateSell = DateTime.UtcNow;
                        freshDbOrder.Status = "SOLD";

                        if (freshDbOrder.PriceBuy.HasValue)
                        {
                            freshDbOrder.ProfitLoss = currentPrice - freshDbOrder.PriceBuy.Value;
                        }

                        var saveResult = await context.SaveChangesAsync();
                        if (saveResult > 0)
                        {
                            lock (_stateLock)
                            {
                                _orderCache.RemoveAll(o => o.Id == orderCache.Id);
                            }

                            _logger.LogInformation(
                                "Sell order executed: {OrderId} at {Price}, Profit: {Profit}",
                                sellOrder.Data.Id,
                                currentPrice,
                                freshDbOrder.ProfitLoss);

                            if (_discordService != null)
                            {
                                await _discordService.LogSellAsync(
                                    config.DisCord_Hook1,
                                    config.DisCord_Hook2,
                                    symbol,
                                    currentPrice,
                                    coinQuantityToSell,
                                    freshDbOrder.ProfitLoss,
                                    sellOrder.Data.Id.ToString());
                            }
                        }
                        else
                        {
                            if (_discordService != null)
                            {
                                await _discordService.LogErrorAsync(
                                    config.DisCord_Hook1,
                                    config.DisCord_Hook2,
                                    $"Failed to save sell order for {symbol}",
                                    $"Order {orderCache.Id} - Binance Order ID: {sellOrder.Data.Id}, Save result: {saveResult}");
                            }

                            // save ไม่สำเร็จ -> revert cache กลับ WAITING_SELL
                            lock (_stateLock)
                            {
                                var cached = _orderCache.FirstOrDefault(o => o.Id == orderCache.Id);
                                if (cached != null)
                                    cached.Status = "WAITING_SELL";
                            }
                        }
                    }
                    catch (Exception saveEx)
                    {
                        if (_discordService != null)
                        {
                            await _discordService.LogErrorAsync(
                                config.DisCord_Hook1,
                                config.DisCord_Hook2,
                                $"Error saving sell order for {symbol}",
                                $"Order {orderCache.Id} - Binance Order ID: {sellOrder.Data.Id}, Error: {saveEx.Message}");
                        }

                        // error ระหว่าง save -> revert cache
                        lock (_stateLock)
                        {
                            var cached = _orderCache.FirstOrDefault(o => o.Id == orderCache.Id);
                            if (cached != null)
                                cached.Status = "WAITING_SELL";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_discordService != null && config != null)
                {
                    await _discordService.LogErrorAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        $"Error in CheckAndSellAsync for {config.SYMBOL}",
                        ex.Message);
                }
            }
        }

        #endregion

        #region Helpers / Infra

        /// <summary>
        /// Reload WAITING_SELL orders from DB into cache (ใช้ตอน Start)
        /// </summary>
        private async Task ReloadOrderCacheFromDbAsync(ApplicationDbContext context, int settingId)
        {
            var orders = await context.DbOrders
                .Where(o => o.Setting_ID == settingId &&
                            o.Status == "WAITING_SELL" &&
                            o.PriceWaitSell.HasValue)
                .ToListAsync();

            orders = orders
                .OrderBy(o => o.PriceWaitSell)
                .Take(20)
                .ToList();

            var cache = orders
                .Select(o => new OrderCache
                {
                    Id = o.Id,
                    OrderBuyID = o.OrderBuyID,
                    PriceBuy = o.PriceBuy,
                    PriceWaitSell = o.PriceWaitSell!.Value,
                    Setting_ID = o.Setting_ID,
                    Status = o.Status,
                    Symbol = o.Symbol
                })
                .ToList();

            lock (_stateLock)
            {
                _orderCache = cache;
            }

            _logger.LogDebug("Order cache loaded: {Count} orders", cache.Count);
        }

        private BinanceRestClient CreateBinanceClient(DbSetting config)
        {
            return new BinanceRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(config.API_KEY!, config.API_SECRET!);
            });
        }

    

        #endregion
    }

    #region Cache model

   

    #endregion

}
