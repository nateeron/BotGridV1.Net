using BotGridV1.Models.SQLite;
using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Threading;

namespace BotGridV1.Services
{
    public class BotWorkerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BotWorkerService> _logger;
        private readonly DiscordService? _discordService;
        private BinanceSocketClient? _socketClient;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning = false;
        private readonly object _lockObject = new object();
        private List<OrderCache> _orderCache = new List<OrderCache>();
        private DateTime _lastBuyTime = DateTime.MinValue;

        private DateTime _lastSellTime = DateTime.MinValue;
        private readonly SemaphoreSlim _sellSemaphore = new(1, 1); // Prevent overlapping sells


        private readonly TimeSpan _minBuyInterval = TimeSpan.FromSeconds(2); // Prevent duplicate buys
        private readonly TimeSpan _minSellInterval = TimeSpan.FromSeconds(1); // Prevent duplicate sells
        private DateTime? _waitBuyTime = null; // เวลาที่เริ่มรอซื้อ (เมื่อไม่มี openSellOrders)
        private DbSetting? _currentConfig;
        private bool _pauseBuyDueToInsufficientBalance = false; // Skip buy until a sell succeeds

        public bool IsBuyPausedDueToInsufficientBalance
        {
            get
            {
                lock (_lockObject)
                {
                    return _pauseBuyDueToInsufficientBalance;
                }
            }
        }

        public void ResetBuyPauseDueToInsufficientBalance()
        {
            lock (_lockObject)
            {
                _pauseBuyDueToInsufficientBalance = false;
            }
            _logger.LogInformation("Buy pause due to insufficient balance has been manually reset.");
        }

        public void SetBuyPauseState(bool pause)
        {
            lock (_lockObject)
            {
                _pauseBuyDueToInsufficientBalance = pause;
            }

            if (pause)
            {
                _logger.LogWarning("Buy logic manually paused.");
            }
            else
            {
                _logger.LogInformation("Buy logic manually resumed.");
            }
        }

        public BotWorkerService(IServiceProvider serviceProvider, ILogger<BotWorkerService> logger, DiscordService? discordService = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _discordService = discordService;
        }
        #region Start Stop
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
                    // Alert to Discord only (no logging to save RAM/CPU)
                    // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                    if (_discordService != null && config != null)
                    {
                        await _discordService.LogErrorAsync(config.DisCord_Hook1, config.DisCord_Hook2,
                            "Configuration not found or API credentials missing", "Bot start failed");
                    }
                    return false;
                }

                if (string.IsNullOrEmpty(config.SYMBOL))
                {
                    // Alert to Discord only (no logging to save RAM/CPU)
                    // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(config.DisCord_Hook1, config.DisCord_Hook2,
                            "Symbol not configured", "Bot start failed");
                    }
                    return false;
                }

                // Store config for Discord logging
                _currentConfig = config;

                // Load initial order cache
                await ReloadOrderCacheAsync(context, config.Id);

                // Create socket client
                _socketClient = new BinanceSocketClient();
                _cancellationTokenSource = new CancellationTokenSource();

                // Subscribe to trade updates
                var symbol = config.SYMBOL;
                var subscription = await _socketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(
                    symbol,
                    async data =>
                    {
                        await ProcessPriceUpdateAsync(data.Data.Price, config);
                    },
                    _cancellationTokenSource.Token);

                if (!subscription.Success)
                {
                    // Alert to Discord only (no logging to save RAM/CPU)
                    // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(config.DisCord_Hook1, config.DisCord_Hook2,
                            $"Failed to subscribe to {symbol}", subscription.Error?.Message ?? "Unknown error");
                    }
                    return false;
                }

                _isRunning = true;
                _logger.LogInformation($"Bot worker started for symbol: {symbol}");

                // Log to Discord
                if (_discordService != null)
                {
                    await _discordService.LogStartAsync(config.DisCord_Hook1, config.DisCord_Hook2, symbol, config.Id);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Alert to Discord only (no logging to save RAM/CPU)
                // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                if (_discordService != null && _currentConfig != null)
                {
                    await _discordService.LogErrorAsync(_currentConfig.DisCord_Hook1, _currentConfig.DisCord_Hook2,
                        "Error starting bot worker", ex.Message);
                }
                return false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                var symbol = _currentConfig?.SYMBOL;
                _cancellationTokenSource?.Cancel();
                _socketClient?.Dispose();
                _socketClient = null;
                _isRunning = false;
                _orderCache.Clear();
                _waitBuyTime = null; // Reset เวลารอซื้อเมื่อ bot หยุด
                _logger.LogInformation("Bot worker stopped");

                // Log to Discord
                if (_discordService != null && _currentConfig != null)
                {
                    await _discordService.LogStopAsync(_currentConfig.DisCord_Hook1, _currentConfig.DisCord_Hook2, symbol);
                }

                _currentConfig = null;
            }
            catch (Exception ex)
            {
                // Alert to Discord only (no logging to save RAM/CPU)
                // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                if (_discordService != null && _currentConfig != null)
                {
                    await _discordService.LogErrorAsync(_currentConfig.DisCord_Hook1, _currentConfig.DisCord_Hook2,
                        "Error stopping bot worker", ex.Message);
                }
            }
        }
        #endregion
        private async Task ProcessPriceUpdateAsync(decimal currentPrice, DbSetting config)
        {
            try
            {
                // Quick check to prevent processing if we just bought (outside lock for performance)
                // ตรวจสอบเบื้องต้นเพื่อป้องกันการประมวลผลถ้าเพิ่งซื้อไป (นอก lock เพื่อประสิทธิภาพ)
                // But allow processing if _lastBuyTime is still at initial value (DateTime.MinValue)
                // แต่ให้ประมวลผลได้ถ้า _lastBuyTime ยังเป็นค่าเริ่มต้น (DateTime.MinValue)
                var timeSinceLastBuy = DateTime.UtcNow - _lastBuyTime;
                if (_lastBuyTime != DateTime.MinValue && timeSinceLastBuy < _minBuyInterval)
                {
                    return; // Too soon after last buy, skip processing
                }

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Reload cache if needed (when sold orders are removed)
                // โหลดแคชใหม่หากจำเป็น (เมื่อคำสั่งขายถูกลบออก)
                lock (_lockObject)
                {
                    if (_orderCache.Count(o => o.Status == "WAITING_SELL") <= 2)
                    {
                        // Will reload after lock
                    }
                }

                // Reload outside lock to avoid blocking
                if (_orderCache.Count(o => o.Status == "WAITING_SELL") <= 2)
                {
                    await ReloadOrderCacheAsync(context, config.Id);
                }

                // Get orders waiting to sell (top 20, sorted by sell price ascending)
                // รับคำสั่งซื้อรอขาย (20 อันดับแรก เรียงตามราคาขายจากน้อยไปมาก)
                List<OrderCache> waitingSellOrders;
                lock (_lockObject)
                {
                    waitingSellOrders = _orderCache
                        .Where(o => o.Status == "WAITING_SELL" && o.Setting_ID == config.Id)
                        .OrderBy(o => o.PriceWaitSell)
                        .Take(20)
                        .ToList();
                }

                // Get last action order (top 1 order by ID desc)
                // รับ order action ล่าสุด (top 1 order by ID desc)
                //var lastActionOrder_XX = await context.DbOrders
                //    .Where(o => o.Setting_ID == config.Id)
                //    .OrderByDescending(o => o.Id)
                //    .FirstOrDefaultAsync();

                var lastActionOrder = await context.DbOrders
                    .Where(o => o.Setting_ID == config.Id &&
                               (o.DateBuy != null || o.DateSell != null))
                    .OrderByDescending(o => o.DateSell ?? o.DateBuy)
                    .FirstOrDefaultAsync();

                // Get open sell orders (Status WAITING_SELL, top 20, order by PriceWaitSell asc)
                // รับ order ขายที่เปิดอยู่ (Status WAITING_SELL, top 20, order by PriceWaitSell asc)
                // SQLite doesn't support decimal in ORDER BY, so we order in memory after fetching
                // SQLite ไม่รองรับ decimal ใน ORDER BY ดังนั้นเราจะเรียงใน memory หลังจากดึงข้อมูล
                var openSellOrders = await context.DbOrders
                    .Where(o => o.Setting_ID == config.Id && o.Status == "WAITING_SELL" && o.PriceWaitSell.HasValue)
                    .ToListAsync();

                openSellOrders = openSellOrders
                    .OrderBy(o => o.PriceWaitSell)
                    .Take(20)
                    .ToList();

                bool shouldCheckBuy = false;
                decimal? buyThreshold = null;

                // Check if last action is a completed order (Buy or Sold, not WAITING_SELL)
                // ตรวจสอบว่า action ล่าสุดเป็น order ที่ถูก Action แล้ว (Buy หรือขายแล้ว ไม่ใช่รอขาย)
                if (lastActionOrder == null)
                {
                    // No orders at all - should buy immediately
                    // ไม่มี order เลย - ควรซื้อทันที
                    shouldCheckBuy = true;
                    _logger.LogInformation($"No orders found in database for Config ID {config.Id}. Will attempt to buy at current price: {currentPrice}");
                }
                // Last action is completed (SOLD or other completed status)
                // Action ล่าสุดเสร็จสมบูรณ์แล้ว (SOLD หรือ status อื่นที่เสร็จแล้ว)
                else if (lastActionOrder.Status == "SOLD" && lastActionOrder.PriceSellActual.HasValue)
                {
                    // Last action is Sold - use PriceSellActual for threshold calculation
                    // Action ล่าสุดเป็นขายแล้ว - ใช้ PriceSellActual ในการคำนวณ threshold
                    // A - (A * 2 / 100)
                    buyThreshold = lastActionOrder.PriceSellActual.Value - (lastActionOrder.PriceSellActual.Value * config.PERCEN_SELL / 100);
                    decimal buyThresholdRunUp_Buy = lastActionOrder.PriceSellActual.Value + (lastActionOrder.PriceSellActual.Value * config.PERCEN_BUY / 100);
                    
                    // ตั้งเวลาเริ่มต้นรอซื้อเมื่อไม่มี openSellOrders
                    // Set initial wait time when there are no openSellOrders
                    if (currentPrice < buyThresholdRunUp_Buy && openSellOrders.Count == 0 && !_waitBuyTime.HasValue)
                    {
                        _waitBuyTime = DateTime.UtcNow;
                    }
                    // Reset เวลารอซื้อเมื่อมี openSellOrders ใหม่
                    // Reset wait time when new openSellOrders appear
                    else if (openSellOrders.Count > 0 && _waitBuyTime.HasValue)
                    {
                        _waitBuyTime = null;
                    }
                    
                    // ตรวจสอบว่าผ่านไป 5 นาทีแล้วหรือยัง (เมื่อไม่มี openSellOrders)
                    // Check if 5 minutes have passed (when there are no openSellOrders)
                    bool wait5MinutesPassed = _waitBuyTime.HasValue && 
                        (DateTime.UtcNow - _waitBuyTime.Value) >= TimeSpan.FromMinutes(5) && 
                        openSellOrders.Count == 0 && currentPrice < buyThresholdRunUp_Buy;
                    
                    if (currentPrice <= buyThreshold || (currentPrice >= buyThresholdRunUp_Buy && openSellOrders.Count == 0) || wait5MinutesPassed)
                    {
                        shouldCheckBuy = true;
                        _waitBuyTime = null;
                    }
                }
                else if (!string.IsNullOrEmpty(lastActionOrder.OrderBuyID) && lastActionOrder.PriceBuy.HasValue)
                {
                    // Last action is Buy (but not SOLD yet) - use PriceBuy for threshold calculation
                    // Action ล่าสุดเป็น Buy (แต่ยังไม่ขาย) - ใช้ PriceBuy ในการคำนวณ threshold
                    //   buyThreshold = lastActionOrder.PriceBuy.Value * (1 - config.PERCEN_BUY / 100);
                    // A - (A * 2 / 100)
                    buyThreshold = lastActionOrder.PriceBuy.Value - (lastActionOrder.PriceBuy.Value * config.PERCEN_BUY / 100);
                    if (currentPrice <= buyThreshold)
                    {
                        shouldCheckBuy = true;
                    }
                }

                // If lastActionOrder.Status == "WAITING_SELL", we don't check buy (waiting to sell)
                // ถ้า lastActionOrder.Status == "WAITING_SELL" เราไม่ตรวจสอบการซื้อ (รอขายอยู่)

                // Skip buy logic until a sell occurs if we previously paused due to low balance
                if (_pauseBuyDueToInsufficientBalance)
                {
                    _logger.LogDebug("Buy logic skipped because bot is paused due to insufficient balance. Waiting for sell before resuming.");
                    shouldCheckBuy = false;
                }

                if (shouldCheckBuy)
                {
                    await CheckAndBuyAsync(context, config, currentPrice, lastActionOrder, openSellOrders);
                }

                // Check if any waiting sell orders should be executed (one at a time)
                // ตรวจสอบว่าควรดำเนินการคำสั่งขายที่รออยู่หรือไม่ (ครั้งละ 1 order)
                await CheckAndSellAsync(context, config, currentPrice);
            }
            catch (Exception ex)
            {
                // Alert to Discord only (no logging to save RAM/CPU)
                // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                if (_discordService != null && config != null)
                {
                    await _discordService.LogErrorAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        $"Error processing price update for {config.SYMBOL}",
                        ex.Message
                    );
                }
            }
        }

        private async Task CheckAndBuyAsync(ApplicationDbContext context, DbSetting config, decimal currentPrice, DbOrder? lastActionOrder = null, List<DbOrder>? openSellOrders = null)
        {
            // Use a flag to track if we successfully acquired the buy lock
            // ใช้ flag เพื่อติดตามว่าเราได้ lock สำหรับการซื้อสำเร็จหรือไม่
            bool buyLockAcquired = false;

            try
            {
                // Critical section: Check and update _lastBuyTime atomically
                // ส่วนสำคัญ: ตรวจสอบและอัปเดต _lastBuyTime แบบ atomic
                lock (_lockObject)
                {
                    // Double check to prevent duplicate buys
                    // ตรวจสอบซ้ำเพื่อป้องกันการซื้อซ้ำ
                    // But allow if _lastBuyTime is still at initial value (first buy ever)
                    // แต่ให้ผ่านได้ถ้า _lastBuyTime ยังเป็นค่าเริ่มต้น (การซื้อครั้งแรก)
                    if (_lastBuyTime != DateTime.MinValue && DateTime.UtcNow - _lastBuyTime < _minBuyInterval)
                    {
                        _logger.LogDebug($"Buy skipped: Too soon after last buy. Time since last buy: {DateTime.UtcNow - _lastBuyTime}");
                        return;
                    }

                    // Update _lastBuyTime BEFORE placing order to prevent race condition
                    // อัปเดต _lastBuyTime ก่อนวางคำสั่งซื้อเพื่อป้องกัน race condition
                    _lastBuyTime = DateTime.UtcNow;
                    buyLockAcquired = true;
                    _logger.LogInformation($"Buy lock acquired at {_lastBuyTime:yyyy-MM-dd HH:mm:ss.fff}. Proceeding with buy order.");
                }

                // If we didn't acquire the lock, return immediately
                // ถ้าเราไม่ได้ lock ให้ return ทันที
                if (!buyLockAcquired)
                {
                    return;
                }

                // Re-validate buy condition using fresh data from database
                // ตรวจสอบเงื่อนไขการซื้ออีกครั้งโดยใช้ข้อมูลใหม่จากฐานข้อมูล
                // This prevents race condition where multiple threads pass initial check
                // นี่ป้องกัน race condition ที่หลาย thread ผ่านการตรวจสอบเบื้องต้น
                //var freshLastActionOrder = await context.DbOrders
                //    .Where(o => o.Setting_ID == config.Id)
                //    .OrderByDescending(o => o.Id)
                //    .FirstOrDefaultAsync();
                var freshLastActionOrder = await context.DbOrders
                    .Where(o => o.Setting_ID == config.Id &&
                               (o.DateBuy != null || o.DateSell != null))
                    .OrderByDescending(o => o.DateSell ?? o.DateBuy)
                    .FirstOrDefaultAsync();
                // Check if there's a very recent order (within last 5 seconds) that might not be in cache yet
                // ตรวจสอบว่ามี order ที่เพิ่งสร้าง (ภายใน 5 วินาทีล่าสุด) ที่อาจยังไม่อยู่ใน cache
                var recentOrder = freshLastActionOrder != null &&
                    freshLastActionOrder.DateBuy.HasValue &&
                    (DateTime.UtcNow - freshLastActionOrder.DateBuy.Value).TotalSeconds < 5
                    ? freshLastActionOrder
                    : null;

                if (recentOrder != null && recentOrder.Status == "WAITING_SELL")
                {
                    _logger.LogWarning($"Buy cancelled: Recent order found (ID: {recentOrder.Id}, Status: {recentOrder.Status}) within 5 seconds. Possible duplicate buy prevented.");
                    return; // Recent order exists, don't buy
                }

                decimal? threshold = null;

                // Check if there are no orders at all - should buy immediately
                // ตรวจสอบว่าไม่มี order เลย - ควรซื้อทันที
                if (freshLastActionOrder == null)
                {
                    // No orders at all - proceed to buy immediately (no threshold check)
                    // ไม่มี order เลย - ดำเนินการซื้อทันที (ไม่ต้องตรวจสอบ threshold)
                    _logger.LogInformation($"No orders found in database. Proceeding to buy immediately at price: {currentPrice}");
                    // threshold remains null, so we skip the threshold check below and proceed to buy
                }
                // Last action is completed (not WAITING_SELL)
                // Action ล่าสุดเสร็จสมบูรณ์แล้ว (ไม่ใช่รอขาย)
                else if (freshLastActionOrder.Status == "SOLD" && freshLastActionOrder.PriceSellActual.HasValue)
                {
                    // Last action is Sold - use PriceSellActual
                    // Action ล่าสุดเป็นขายแล้ว - ใช้ PriceSellActual

                    threshold = freshLastActionOrder.PriceSellActual.Value - (freshLastActionOrder.PriceSellActual.Value * config.PERCEN_BUY / 100);
                }
                else if (!string.IsNullOrEmpty(freshLastActionOrder.OrderBuyID) && freshLastActionOrder.PriceBuy.HasValue)
                {
                    // Last action is Buy - use PriceBuy
                    // Action ล่าสุดเป็น Buy - ใช้ PriceBuy
                    threshold = freshLastActionOrder.PriceBuy.Value - (freshLastActionOrder.PriceBuy.Value * config.PERCEN_BUY / 100);
                }
                else
                {
                    // Last action is WAITING_SELL - don't buy
                    // Action ล่าสุดเป็น WAITING_SELL - ไม่ซื้อ
                    _logger.LogDebug($"Buy cancelled: Last action order is WAITING_SELL (ID: {freshLastActionOrder.Id})");
                    return;
                }
                decimal buyThresholdRunUp_Buy = 0;
                // 1) คำนวณ RunUp เฉพาะเมื่อ lastActionOrder ไม่ใช่ null และ PriceSellActual != null
                if (lastActionOrder != null && lastActionOrder.PriceSellActual != null && openSellOrders.Count() == 0)
                {
                    decimal lastPrice = lastActionOrder.PriceSellActual.Value;
                    buyThresholdRunUp_Buy = lastPrice + (lastPrice * config.PERCEN_BUY / 100);
                }

                // Only check threshold if it was set (not null)
                // ถ้า threshold = null (ไม่มี order) ให้ซื้อทันที
                // ตรวจสอบ threshold เฉพาะเมื่อมีการตั้งค่า (ไม่ใช่ null)
                if (threshold.HasValue && currentPrice > threshold.Value && !(buyThresholdRunUp_Buy > 0 && currentPrice >= buyThresholdRunUp_Buy && openSellOrders.Count() == 0))
                {
                    _logger.LogDebug($"Buy cancelled: Price {currentPrice} is above threshold {threshold.Value}");
                    return; // Price hasn't dropped enough ราคายังไม่ลดลงเพียงพอ
                }

                // If we reach here and threshold is null, it means no orders exist - proceed to buy
                // ถ้าเรามาถึงจุดนี้และ threshold เป็น null หมายความว่าไม่มี order - ดำเนินการซื้อ
                if (threshold == null)
                {
                    _logger.LogInformation($"No threshold check needed (no orders in database). Proceeding to buy at price: {currentPrice}");
                }

                // Create Binance client 
                // สร้างไคลเอนต์ Binance
                var restClient = CreateBinanceClient(config);
                var symbol = config.SYMBOL!;

                // Get buy amount from database configuration
                // รับจำนวนเงินซื้อจากฐานข้อมูลการตั้งค่า
                if (!config.BuyAmountUSD.HasValue || config.BuyAmountUSD.Value <= 0)
                {
                    _logger.LogWarning("BuyAmountUSD is not configured or invalid");
                    return;
                }

                var buyAmountUSD = config.BuyAmountUSD.Value;

                // Check account balance
                // ตรวจสอบยอดคงเหลือในบัญชี
                var accountInfo = await restClient.SpotApi.Account.GetAccountInfoAsync();
                if (!accountInfo.Success)
                {
                    // Alert to Discord only (no logging to save RAM/CPU)
                    // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Failed to get account info for {symbol}",
                            accountInfo.Error?.Message ?? "Unknown error"
                        );
                    }
                    return;
                }

                var usdtBalance = accountInfo.Data.Balances.FirstOrDefault(b => b.Asset == "USDT");
                if (usdtBalance == null || usdtBalance.Available < buyAmountUSD)
                {
                    _logger.LogWarning($"Insufficient USDT balance. Required: {buyAmountUSD}, Available: {usdtBalance?.Available ?? 0}");

                    // Pause buy logic until next successful sell
                    _pauseBuyDueToInsufficientBalance = true;

                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Insufficient USDT balance - Stopping bot for {symbol}",
                            $"Required: {buyAmountUSD}, Available: {usdtBalance?.Available ?? 0}"
                        );
                    }
                    // Stop the bot when balance is insufficient
                    // หยุด Bot เมื่อยอดไม่พอ
                    //  await StopAsync();
                    return;
                }

                // Calculate coin quantity from USD amount
                // คำนวณจำนวน Coin จากจำนวนเงิน USD
                // var quantity = CalculateCoinQuantity(buyAmountUSD, currentPrice, symbol);

                // Place market buy order
                // วางคำสั่งซื้อในตลาด
                var buyOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: OrderSide.Buy,
                    type: SpotOrderType.Market,
                    quoteQuantity: buyAmountUSD);

                if (!buyOrder.Success)
                {
                    // Alert to Discord only (no logging to save RAM/CPU)
                    // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                    if (_discordService != null)
                    {
                        await _discordService.LogBuyNotSuccessAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            symbol,
                            buyOrder.Error?.Message ?? "Unknown error",
                            0
                        );
                    }

                    // Retry buy logic - wait a bit and try again
                    // ลอจิกการซื้อซ้ำ - รอสักครู่แล้วลองอีกครั้ง
                    await Task.Delay(1000); // Wait 1 second before retry

                    // Log Buy Retry to Discord
                    if (_discordService != null)
                    {
                        await _discordService.LogBuyRetryAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            symbol,
                            currentPrice,
                            1,
                            buyOrder.Error?.Message ?? "Retrying after failure"
                        );
                    }

                    // Retry the buy order once
                    var retryBuyOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                        symbol: symbol,
                        side: OrderSide.Buy,
                        type: SpotOrderType.Market,
                        quoteQuantity: buyAmountUSD);

                    if (!retryBuyOrder.Success)
                    {
                        // Alert to Discord only (no logging to save RAM/CPU)
                        // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                        if (_discordService != null)
                        {
                            await _discordService.LogBuyNotSuccessAsync(
                                config.DisCord_Hook1,
                                config.DisCord_Hook2,
                                symbol,
                                retryBuyOrder.Error?.Message ?? "Retry failed",
                                1
                            );
                        }
                        return;
                    }

                    // Use retry order if successful
                    buyOrder = retryBuyOrder;
                }

                // Get actual coin quantity from buy order response
                // รับจำนวน Coin จริงจากคำตอบคำสั่งซื้อ
                var actualCoinQuantity = buyOrder.Data.QuantityFilled > 0
                    ? buyOrder.Data.QuantityFilled
                    : buyOrder.Data.Quantity;

                // Calculate sell price with PERCEN_SELL
                // คำนวณราคาขายด้วย PERCEN_SELL
                var sellPrice = currentPrice + (currentPrice * config.PERCEN_SELL / 100);

                // Create order record
                // สร้างบันทึกการสั่งซื้อ
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
                    Quantity = actualCoinQuantity, // Actual quantity from order response
                    BuyAmountUSD = buyAmountUSD, // จำนวนเงินซื้อขาย (USD)
                    CoinQuantity = actualCoinQuantity // จำนวนCoinSell - จำนวน Coin ที่ซื้อมาจริง
                };

                context.DbOrders.Add(dbOrder);
                await context.SaveChangesAsync();

                // Reset เวลารอซื้อเมื่อซื้อสำเร็จ
                // Reset wait buy time when buy is successful
                _waitBuyTime = null;

                // Update cache (note: _lastBuyTime was already updated before placing order)
                // อัปเดตแคช (หมายเหตุ: _lastBuyTime ถูกอัปเดตแล้วก่อนวางคำสั่งซื้อ)
                lock (_lockObject)
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

                _logger.LogInformation($"Buy order placed: {buyOrder.Data.Id} at {currentPrice}, Sell target: {sellPrice}");

                // Log Buy Success to Discord
                if (_discordService != null)
                {
                    await _discordService.LogBuyAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        symbol,
                        currentPrice,
                        actualCoinQuantity,
                        buyAmountUSD,
                        buyOrder.Data.Id.ToString()
                    );
                }
            }
            catch (Exception ex)
            {
                // Alert to Discord only (no logging to save RAM/CPU)
                // แจ้งเตือนไปยัง Discord เท่านั้น (ไม่ log เพื่อประหยัด RAM/CPU)
                if (_discordService != null && config != null)
                {
                    await _discordService.LogErrorAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        $"Error in CheckAndBuyAsync for {config.SYMBOL}",
                        ex.Message
                    );
                }
            }
        }
        
        [HttpPost]
        public async Task Test_sellOnly()
        {

            // make data 

            //await CheckAndSellAsync(context, config, currentPrice);

             
        }

        #region no
        private async Task CheckAndSellAsync(ApplicationDbContext context, DbSetting config, decimal currentPrice)
        {
            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = await _sellSemaphore.WaitAsync(0);
                if (!semaphoreAcquired)
                {
                    return; // Another sell is in progress, skip to avoid duplicate processing
                }

                if (_lastSellTime != DateTime.MinValue && DateTime.UtcNow - _lastSellTime < _minSellInterval)
                {
                    return; // Prevent rapid consecutive sells
                }

                OrderCache? orderToSell = null;

                // พยายามหยิบ order ที่ถึงเป้าจาก cache ก่อน
                // Try to grab target order from cache first
                lock (_lockObject)
                {
                    orderToSell = _orderCache
                        .Where(o => o.Status == "WAITING_SELL" &&
                                    o.Setting_ID == config.Id &&
                                    currentPrice >= o.PriceWaitSell)
                        .OrderBy(o => o.PriceWaitSell)
                        .FirstOrDefault();
                }

                if (orderToSell == null)
                {
                    // ถ้าไม่เจอใน cache ให้โหลดจากฐานข้อมูล (ครั้งละ 1 order ราคาเป้าต่ำสุด)
                    // If not found in cache, load from database (single order with lowest target)
                    var fallbackCandidates = await context.DbOrders
                        .Where(o => o.Setting_ID == config.Id &&
                                    o.Status == "WAITING_SELL" &&
                                    o.PriceWaitSell.HasValue &&
                                    currentPrice >= o.PriceWaitSell.Value)
                        .ToListAsync();

                    var fallbackOrder = fallbackCandidates
                        .OrderBy(o => o.PriceWaitSell)
                        .FirstOrDefault();

                    if (fallbackOrder == null)
                    {
                        return; // No orders reached target
                    }

                    orderToSell = new OrderCache
                    {
                        Id = fallbackOrder.Id,
                        OrderBuyID = fallbackOrder.OrderBuyID,
                        PriceBuy = fallbackOrder.PriceBuy,
                        PriceWaitSell = fallbackOrder.PriceWaitSell ?? 0,
                        Setting_ID = fallbackOrder.Setting_ID,
                        Status = fallbackOrder.Status,
                        Symbol = fallbackOrder.Symbol
                    };
                }

                var dbOrder = await context.DbOrders.FindAsync(orderToSell.Id);
                if (dbOrder == null)
                {
                    lock (_lockObject)
                    {
                        _orderCache.RemoveAll(o => o.Id == orderToSell.Id);
                    }
                    return;
                }

                if (dbOrder.Status != "WAITING_SELL")
                {
                    lock (_lockObject)
                    {
                        _orderCache.RemoveAll(o => o.Id == orderToSell.Id);
                    }
                    return;
                }

                var restClient = CreateBinanceClient(config);
                var symbol = dbOrder.Symbol ?? config.SYMBOL!;
                var waitingSellCount = await context.DbOrders
                    .CountAsync(o => o.Setting_ID == config.Id && o.Status == "WAITING_SELL");
                var isLastWaitingOrder = waitingSellCount <= 1;

                // Place market sell order using coin quantity from database
                // วางคำสั่งขายในตลาดโดยใช้จำนวน Coin จากฐานข้อมูล
                var coinQuantityToSell = dbOrder.CoinQuantity ?? dbOrder.Quantity ?? 0;
                if (coinQuantityToSell <= 0)
                {
                    if (isLastWaitingOrder)
                    {
                        await ForceMarkOrderAsSoldAsync(
                            context,
                            dbOrder,
                            config,
                            currentPrice,
                            0,
                            $"Forced close: coin quantity is zero for final order {dbOrder.Id}."
                        );
                    }
                    else if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Invalid coin quantity for order {dbOrder.Id}",
                            $"CoinQuantity: {dbOrder.CoinQuantity}, Quantity: {dbOrder.Quantity}"
                        );
                    }
                    return;
                }

                // If this is the last waiting sell order, adjust quantity to available balance
                if (isLastWaitingOrder)
                {
                    var baseAsset = GetBaseAssetFromSymbol(symbol);
                    var accountInfo = await restClient.SpotApi.Account.GetAccountInfoAsync();
                    if (accountInfo.Success)
                    {
                        var baseBalance = accountInfo.Data.Balances
                            .FirstOrDefault(b => string.Equals(b.Asset, baseAsset, StringComparison.OrdinalIgnoreCase));

                        var availableBalance = baseBalance?.Available ?? 0m;
                        if (availableBalance <= 0)
                        {
                            await ForceMarkOrderAsSoldAsync(
                                context,
                                dbOrder,
                                config,
                                currentPrice,
                                0,
                                $"Forced close: No {baseAsset} balance available for final order {dbOrder.Id}."
                            );
                            return;
                        }

                        if (availableBalance < coinQuantityToSell)
                        {
                            coinQuantityToSell = availableBalance;
                        }
                    }
                    else if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Failed to get account info for {symbol}",
                            accountInfo.Error?.Message ?? "Unknown error"
                        );
                    }
                }

                if (coinQuantityToSell <= 0)
                {
                    await ForceMarkOrderAsSoldAsync(
                        context,
                        dbOrder,
                        config,
                        currentPrice,
                        0,
                        $"Forced close: Adjusted coin quantity is zero for order {dbOrder.Id}."
                    );
                    return;
                }

                var sellOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
                    symbol: symbol,
                    side: OrderSide.Sell,
                    type: SpotOrderType.Market,
                    quantity: coinQuantityToSell);

                if (!sellOrder.Success)
                {
                    if (isLastWaitingOrder && IsQuantityTooLowError(sellOrder.Error?.Message))
                    {
                        var forcedOrder = await context.DbOrders.FindAsync(orderToSell.Id);
                        if (forcedOrder != null)
                        {
                            await ForceMarkOrderAsSoldAsync(
                                context,
                                forcedOrder,
                                config,
                                currentPrice,
                                coinQuantityToSell,
                                $"Forced close: Binance rejected final sell order due to quantity constraint ({sellOrder.Error?.Message ?? "unknown reason"})."
                            );
                        }
                    }
                    else if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Sell order failed for {symbol}",
                            sellOrder.Error?.Message ?? "Unknown error"
                        );
                    }
                    return;
                }

                var freshDbOrder = await context.DbOrders.FindAsync(orderToSell.Id);
                if (freshDbOrder == null || freshDbOrder.Status != "WAITING_SELL")
                {
                    if (_discordService != null)
                    {
                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Sell order conflict for {symbol}",
                            $"Order {orderToSell.Id} was already sold. Binance Order ID: {sellOrder.Data.Id}"
                        );
                    }
                    return;
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
                        lock (_lockObject)
                        {
                            _orderCache.RemoveAll(o => o.Id == orderToSell.Id);
                        }

                        _lastSellTime = DateTime.UtcNow;

                        _logger.LogInformation($"Sell order executed: {sellOrder.Data.Id} at {currentPrice}, Profit: {freshDbOrder.ProfitLoss}");

                        if (_pauseBuyDueToInsufficientBalance)
                        {
                            _pauseBuyDueToInsufficientBalance = false;
                            _logger.LogInformation("Buy logic resumed: A sell completed after insufficient balance pause.");
                        }

                        if (_discordService != null)
                        {
                            await _discordService.LogSellAsync(
                                config.DisCord_Hook1,
                                config.DisCord_Hook2,
                                symbol,
                                currentPrice,
                                coinQuantityToSell,
                                freshDbOrder.ProfitLoss,
                                sellOrder.Data.Id.ToString()
                            );
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
                                $"Order {orderToSell.Id} - Binance Order ID: {sellOrder.Data.Id}, Save result: {saveResult}"
                            );
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
                            $"Order {orderToSell.Id} - Binance Order ID: {sellOrder.Data.Id}, Error: {saveEx.Message}"
                        );
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
                        ex.Message
                    );
                }
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _sellSemaphore.Release();
                }
            }
        }

        private string GetBaseAssetFromSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return symbol;
            }

            var knownQuotes = new[] { "USDT", "BUSD", "USDC", "BTC", "ETH", "BNB", "TRY", "EUR", "GBP", "AUD" };
            foreach (var quote in knownQuotes)
            {
                if (symbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase))
                {
                    return symbol.Substring(0, symbol.Length - quote.Length);
                }
            }

            return symbol.Length > 3 ? symbol.Substring(0, symbol.Length - 3) : symbol;
        }

        private bool IsQuantityTooLowError(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            var normalized = message.ToLowerInvariant();
            return normalized.Contains("insufficient") ||
                   normalized.Contains("lot size") ||
                   normalized.Contains("notional") ||
                   normalized.Contains("min");
        }

        private async Task ForceMarkOrderAsSoldAsync(
            ApplicationDbContext context,
            DbOrder order,
            DbSetting config,
            decimal currentPrice,
            decimal coinQuantityUsed,
            string reason)
        {
            try
            {
                order.PriceSellActual = currentPrice;
                order.DateSell = DateTime.UtcNow;
                order.Status = "SOLD";
                order.OrderSellID ??= $"FORCED_CLOSE_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

                if (order.PriceBuy.HasValue)
                {
                    order.ProfitLoss = currentPrice - order.PriceBuy.Value;
                }

                var saveResult = await context.SaveChangesAsync();
                if (saveResult > 0)
                {
                    lock (_lockObject)
                    {
                        _orderCache.RemoveAll(o => o.Id == order.Id);
                    }

                    _lastSellTime = DateTime.UtcNow;

                    if (_pauseBuyDueToInsufficientBalance)
                    {
                        _pauseBuyDueToInsufficientBalance = false;
                    }

                    if (_discordService != null)
                    {
                        await _discordService.LogSellAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            order.Symbol ?? config.SYMBOL!,
                            currentPrice,
                            coinQuantityUsed,
                            order.ProfitLoss,
                            order.OrderSellID);

                        await _discordService.LogErrorAsync(
                            config.DisCord_Hook1,
                            config.DisCord_Hook2,
                            $"Forced sell completion for {order.Symbol ?? config.SYMBOL!}",
                            reason);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_discordService != null)
                {
                    await _discordService.LogErrorAsync(
                        config.DisCord_Hook1,
                        config.DisCord_Hook2,
                        $"Error forcing sell completion for {order.Symbol ?? config.SYMBOL!}",
                        ex.Message);
                }
            }
        }

        private async Task ReloadOrderCacheAsync(ApplicationDbContext context, int settingId)
        {
            try
            {
                // SQLite doesn't support decimal in ORDER BY, so we order in memory after fetching
                // SQLite ไม่รองรับ decimal ใน ORDER BY ดังนั้นเราจะเรียงใน memory หลังจากดึงข้อมูล
                var orders = await context.DbOrders
                    .Where(o => o.Setting_ID == settingId && o.Status == "WAITING_SELL" && o.PriceWaitSell.HasValue)
                    .ToListAsync();

                orders = orders
                    .OrderBy(o => o.PriceWaitSell)
                    .Take(20)
                    .ToList();

                lock (_lockObject)
                {
                    _orderCache = orders
                        .Where(o => o.PriceWaitSell.HasValue)
                        .Select(o => new OrderCache
                        {
                            Id = o.Id,
                            OrderBuyID = o.OrderBuyID,
                            PriceBuy = o.PriceBuy,
                            PriceWaitSell = o.PriceWaitSell!.Value,
                            Setting_ID = o.Setting_ID,
                            Status = o.Status,
                            Symbol = o.Symbol
                        }).ToList();
                }

                _logger.LogDebug($"Order cache reloaded: {orders.Count} orders");
            }
            catch (Exception ex)
            {
                // No logging to save RAM/CPU (silent failure for cache reload)
                // ไม่ log เพื่อประหยัด RAM/CPU (ล้มเหลวเงียบๆ สำหรับการ reload cache)
            }
        }

        /// <summary>
        /// Calculate coin quantity from USD amount
        /// คำนวณจำนวน Coin จากจำนวนเงิน USD
        /// </summary>
        private decimal CalculateCoinQuantity(decimal usdAmount, decimal currentPrice, string symbol)
        {
            // Calculate base quantity
            // คำนวณจำนวนพื้นฐาน
            var quantity = usdAmount / currentPrice;

            // Round down to appropriate decimal places based on symbol
            // ปัดเศษลงตามตำแหน่งทศนิยมที่เหมาะสมตามสัญลักษณ์
            // Most coins use 8 decimal places, but we'll use a safe default
            // เหรียญส่วนใหญ่ใช้ 8 ตำแหน่งทศนิยม แต่เราจะใช้ค่าเริ่มต้นที่ปลอดภัย
            var decimals = 4;

            // Round down to avoid exceeding available balance
            // ปัดเศษลงเพื่อหลีกเลี่ยงการเกินยอดคงเหลือ
            var multiplier = (decimal)Math.Pow(10, decimals);
            quantity = Math.Floor(quantity * multiplier) / multiplier;

            return quantity;
        }

        private BinanceRestClient CreateBinanceClient(DbSetting config)
        {
            return new BinanceRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(config.API_KEY!, config.API_SECRET!);
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Background service is managed by Start/Stop methods
            await Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await StopAsync();
            await base.StopAsync(cancellationToken);
        }
        #endregion
    }
    #region class
    public class OrderCache
    {
        public int Id { get; set; }
        public string? OrderBuyID { get; set; }
        public decimal? PriceBuy { get; set; }
        public decimal PriceWaitSell { get; set; }
        public int Setting_ID { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Symbol { get; set; }
    }
    #endregion
}


