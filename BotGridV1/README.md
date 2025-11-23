# BotGridV1 - Binance Spot Trading Bot

ระบบ Trading Bot สำหรับ Binance Spot ที่ทำงานแบบ Grid Trading โดยใช้ WebSocket เพื่อรับราคาแบบ Real-time และทำการซื้อขายอัตโนมัติตามเงื่อนไขที่กำหนด

## 📋 สารบัญ

- [ภาพรวมระบบ](#ภาพรวมระบบ)
- [ความต้องการของระบบ](#ความต้องการของระบบ)
- [การติดตั้ง](#การติดตั้ง)
- [การตั้งค่าเริ่มต้น](#การตั้งค่าเริ่มต้น)
- [การทำงานเมื่อ Start](#การทำงานเมื่อ-start)
- [API Endpoints](#api-endpoints)
- [โครงสร้างฐานข้อมูล](#โครงสร้างฐานข้อมูล)
- [Logic การซื้อขาย](#logic-การซื้อขาย)
- [Discord Logging](#discord-logging)

---

## 🎯 ภาพรวมระบบ

BotGridV1 เป็นระบบ Trading Bot ที่:
- รับราคา Real-time ผ่าน Binance WebSocket
- ทำการซื้อขายอัตโนมัติตามเงื่อนไข PERCEN_BUY และ PERCEN_SELL
- เก็บข้อมูล Order ลง SQLite Database
- ส่ง Log ไปยัง Discord Webhook
- รองรับการทำงานหลาย Config พร้อมกัน

---

## 💻 ความต้องการของระบบ

- .NET 8.0 SDK
- Binance API Key และ Secret (ต้องมีสิทธิ์ Spot Trading)
- Discord Webhook URL (optional แต่แนะนำ)

---

## 🚀 การติดตั้ง

### 1. Clone หรือ Download โปรเจกต์

```bash
cd BotGridV1
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Build โปรเจกต์

```bash
dotnet build
```

### 4. Run โปรเจกต์

```bash
dotnet run
```

หรือ

```bash
dotnet run --urls "http://0.0.0.0:5081"
```

---

## ⚙️ การตั้งค่าเริ่มต้น

### 1. สร้าง Database และ Table

เรียก API เพื่อสร้าง Database:

```http
POST http://localhost:5081/api/SQLite/CheckAndCreateDatabase
```

### 2. ตั้งค่า Configuration

เรียก API เพื่อสร้างหรืออัปเดต Configuration:

```http
POST http://localhost:5081/api/SQLite/CreateSetting
Content-Type: application/json

{
  "Config_Version": 1,
  "API_KEY": "your_binance_api_key",
  "API_SECRET": "your_binance_api_secret",
  "DisCord_Hook1": "https://discord.com/api/webhooks/...",
  "DisCord_Hook2": "https://discord.com/api/webhooks/...",
  "SYMBOL": "XRPUSDT",
  "PERCEN_BUY": 1.25,
  "PERCEN_SELL": 0.45,
  "BuyAmountUSD": 17.5
}
```

### 3. ตรวจสอบ Configuration

```http
POST http://localhost:5081/api/SQLite/GetById
Content-Type: application/json

{
  "id": 1
}
```

---

## 🎬 การทำงานเมื่อ Start

เมื่อเรียก API `POST /api/BotWorker/Start` ระบบจะทำงานตามลำดับดังนี้:

### ขั้นตอนที่ 1: ตรวจสอบสถานะ

```csharp
if (_isRunning) {
    return false; // Bot กำลังทำงานอยู่แล้ว
}
```

### ขั้นตอนที่ 2: สร้าง Database (ถ้ายังไม่มี)

```csharp
await context.Database.EnsureCreatedAsync();
```

- สร้างไฟล์ `botgrid.db` (SQLite Database)
- สร้าง Table `db_setting` และ `db_Order` อัตโนมัติ

### ขั้นตอนที่ 3: โหลด Configuration

```csharp
var config = configId.HasValue
    ? await context.DbSettings.FindAsync(configId.Value)
    : await context.DbSettings.FirstOrDefaultAsync();
```

- ถ้ามี `ConfigId` จะใช้ Config นั้น
- ถ้าไม่มี จะใช้ Config แรกที่เจอ

### ขั้นตอนที่ 4: ตรวจสอบ Configuration

ระบบจะตรวจสอบ:

1. **API Credentials**
   ```csharp
   if (config == null || string.IsNullOrEmpty(config.API_KEY) || string.IsNullOrEmpty(config.API_SECRET))
   ```
   - ต้องมี API_KEY และ API_SECRET
   - ถ้าไม่มี → Log Error → ส่งไป Discord → Return false

2. **Symbol**
   ```csharp
   if (string.IsNullOrEmpty(config.SYMBOL))
   ```
   - ต้องมี SYMBOL (เช่น XRPUSDT, BTCUSDT)
   - ถ้าไม่มี → Log Error → ส่งไป Discord → Return false

### ขั้นตอนที่ 5: โหลด Order Cache

```csharp
await ReloadOrderCacheAsync(context, config.Id);
```

- ดึง Order ที่ Status = "WAITING_SELL" จาก Database
- เรียงตาม `PriceWaitSell` ascending
- เก็บไว้ใน Memory Cache (สูงสุด 20 orders)
- ใช้สำหรับตรวจสอบเงื่อนไขการซื้อขาย

### ขั้นตอนที่ 6: สร้าง WebSocket Connection

```csharp
_socketClient = new BinanceSocketClient();
_cancellationTokenSource = new CancellationTokenSource();

var subscription = await _socketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(
    symbol,
    async data => {
        await ProcessPriceUpdateAsync(data.Data.Price, config);
    },
    _cancellationTokenSource.Token);
```

- สร้าง BinanceSocketClient
- Subscribe ไปยัง Trade Updates ของ Symbol ที่กำหนด
- ทุกครั้งที่มี Trade ใหม่ → เรียก `ProcessPriceUpdateAsync`

### ขั้นตอนที่ 7: ตั้งสถานะและ Log

```csharp
_isRunning = true;
_logger.LogInformation($"Bot worker started for symbol: {symbol}");

// ส่งไป Discord
await _discordService.LogStartAsync(...);
```

- ตั้ง `_isRunning = true`
- Log ไปยัง Console
- ส่ง Notification ไปยัง Discord Webhook

---

## 📡 การทำงาน Real-time (ProcessPriceUpdateAsync)

เมื่อได้รับราคาใหม่จาก WebSocket ระบบจะทำงานดังนี้:

### 1. ตรวจสอบ Duplicate Buy Prevention

```csharp
if (DateTime.UtcNow - _lastBuyTime < _minBuyInterval) {
    return; // รอ 2 วินาทีก่อนซื้อครั้งถัดไป
}
```

### 2. Reload Order Cache (ถ้าจำเป็น)

```csharp
if (_orderCache.Count(o => o.Status == "WAITING_SELL") <= 2) {
    await ReloadOrderCacheAsync(context, config.Id);
}
```

- ถ้ามี Order รอขายเหลือ <= 2 → โหลดใหม่จาก Database

### 3. ดึง Last Action Order และ Open Sell Orders

```csharp
// Last Action Order (Top 1, Order by ID desc)
var lastActionOrder = await context.DbOrders
    .Where(o => o.Setting_ID == config.Id)
    .OrderByDescending(o => o.Id)
    .FirstOrDefaultAsync();

// Open Sell Orders (Status WAITING_SELL, Top 20, Order by PriceWaitSell asc)
var openSellOrders = await context.DbOrders
    .Where(o => o.Setting_ID == config.Id && o.Status == "WAITING_SELL" && o.PriceWaitSell.HasValue)
    .OrderBy(o => o.PriceWaitSell)
    .Take(20)
    .ToListAsync();
```

### 4. คำนวณ Buy Threshold

**กรณีที่ 1: Last Action เป็น Buy Order**
```csharp
if (lastActionOrder != null && !string.IsNullOrEmpty(lastActionOrder.OrderBuyID) && lastActionOrder.PriceBuy.HasValue) {
    buyThreshold = lastActionOrder.PriceBuy.Value * (1 - config.PERCEN_BUY / 100);
    // ตัวอย่าง: PriceBuy = 2.0447, PERCEN_BUY = 1.25%
    // buyThreshold = 2.0447 * (1 - 1.25/100) = 2.0191425
}
```

**กรณีที่ 2: Last Action ไม่ใช่ Buy หรือไม่มี Last Action**
```csharp
else if (openSellOrders.Any()) {
    var lowestSellPrice = openSellOrders.First().PriceWaitSell!.Value;
    buyThreshold = lowestSellPrice * (1 - config.PERCEN_BUY / 100);
}
```

**กรณีที่ 3: ไม่มี Order เลย**
```csharp
else {
    shouldCheckBuy = true; // ตรวจสอบว่าควรซื้อหรือไม่
}
```

### 5. ตรวจสอบเงื่อนไขการซื้อ

```csharp
if (currentPrice <= buyThreshold) {
    await CheckAndBuyAsync(context, config, currentPrice, lastActionOrder, openSellOrders);
}
```

### 6. ตรวจสอบเงื่อนไขการขาย

```csharp
await CheckAndSellAsync(context, config, currentPrice, sellOrdersToCheck);
```

---

## 💰 Logic การซื้อ (CheckAndBuyAsync)

### 1. ตรวจสอบ Duplicate Buy Prevention (อีกครั้ง)

```csharp
if (DateTime.UtcNow - _lastBuyTime < _minBuyInterval) {
    return; // ป้องกันการซื้อซ้ำ
}
```

### 2. ตรวจสอบ Buy Threshold

```csharp
// Re-validate using lastActionOrder or openSellOrders
if (threshold.HasValue && currentPrice > threshold.Value) {
    return; // ราคายังไม่ลดลงเพียงพอ
}
```

### 3. ตรวจสอบ BuyAmountUSD

```csharp
if (!config.BuyAmountUSD.HasValue || config.BuyAmountUSD.Value <= 0) {
    return; // ไม่มีการตั้งค่าจำนวนเงินซื้อ
}
```

### 4. ตรวจสอบยอด USDT

```csharp
var usdtBalance = accountInfo.Data.Balances.FirstOrDefault(b => b.Asset == "USDT");
if (usdtBalance == null || usdtBalance.Available < buyAmountUSD) {
    // ส่ง Error ไป Discord
    await StopAsync(); // หยุด Bot
    return;
}
```

**⚠️ สำคัญ: ถ้ายอด USDT ไม่พอ ระบบจะหยุดทำงานทันที**

### 5. วางคำสั่งซื้อ

```csharp
var buyOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
    symbol: symbol,
    side: OrderSide.Buy,
    type: SpotOrderType.Market,
    quoteQuantity: buyAmountUSD);
```

- ใช้ `quoteQuantity` (จำนวน USD) แทน `quantity` (จำนวน Coin)
- Binance จะคำนวณจำนวน Coin ให้อัตโนมัติ

### 6. Retry Logic (ถ้าซื้อไม่สำเร็จ)

```csharp
if (!buyOrder.Success) {
    // Log Buy Not Success → Discord
    await Task.Delay(1000); // รอ 1 วินาที
    // Log Buy Retry → Discord
    // Retry 1 ครั้ง
    if (!retryBuyOrder.Success) {
        // Log Buy Not Success (Retry) → Discord
        return;
    }
}
```

### 7. เก็บข้อมูลลง Database

```csharp
var dbOrder = new DbOrder {
    Timestamp = DateTime.UtcNow,
    OrderBuyID = buyOrder.Data.Id.ToString(),
    PriceBuy = currentPrice,
    PriceWaitSell = currentPrice * (1 + config.PERCEN_SELL / 100),
    DateBuy = DateTime.UtcNow,
    Setting_ID = config.Id,
    Status = "WAITING_SELL",
    Symbol = symbol,
    Quantity = actualCoinQuantity,
    BuyAmountUSD = buyAmountUSD,
    CoinQuantity = actualCoinQuantity
};
```

### 8. อัปเดต Cache และ Log

```csharp
_orderCache.Add(new OrderCache { ... });
_lastBuyTime = DateTime.UtcNow;
// Log Buy Success → Discord
```

---

## 💸 Logic การขาย (CheckAndSellAsync)

### 1. วนลูป Order ที่รอขาย

```csharp
foreach (var orderCache in waitingSellOrders) {
    if (currentPrice >= orderCache.PriceWaitSell) {
        // ราคาถึงเป้าขายแล้ว
    }
}
```

### 2. ตรวจสอบ Order Status

```csharp
var dbOrder = await context.DbOrders.FindAsync(orderCache.Id);
if (dbOrder == null || dbOrder.Status != "WAITING_SELL") {
    continue; // ข้าม order นี้
}
```

### 3. วางคำสั่งขาย

```csharp
var coinQuantityToSell = dbOrder.CoinQuantity ?? dbOrder.Quantity ?? 0;
var sellOrder = await restClient.SpotApi.Trading.PlaceOrderAsync(
    symbol: symbol,
    side: OrderSide.Sell,
    type: SpotOrderType.Market,
    quantity: coinQuantityToSell);
```

- ใช้ `CoinQuantity` จาก Database (จำนวน Coin ที่ซื้อมาจริง)

### 4. อัปเดต Order Status

```csharp
dbOrder.OrderSellID = sellOrder.Data.Id.ToString();
dbOrder.PriceSellActual = currentPrice;
dbOrder.DateSell = DateTime.UtcNow;
dbOrder.Status = "SOLD";
dbOrder.ProfitLoss = currentPrice - dbOrder.PriceBuy.Value;
```

### 5. ลบออกจาก Cache และ Log

```csharp
_orderCache.RemoveAll(o => o.Id == orderCache.Id);
// Log Sell Success → Discord
```

---

## 📊 API Endpoints

### Bot Worker APIs

#### Start Bot
```http
POST /api/BotWorker/Start
Content-Type: application/json

{
  "ConfigId": 1  // Optional, ถ้าไม่ระบุจะใช้ Config แรก
}
```

#### Check Status
```http
POST /api/BotWorker/CheckStatus
```

#### Stop Bot
```http
POST /api/BotWorker/Stop
```

#### Get Real-time Price
```http
POST /api/BotWorker/GetPriceRealtime
Content-Type: application/json

{
  "Symbol": "XRPUSDT"  // Optional, default: XRPUSDT
}
```

### SQLite APIs

#### Check/Create Database
```http
POST /api/SQLite/CheckAndCreateDatabase
```

#### Get All Settings
```http
GET /api/SQLite/GetAll
```

#### Get Setting by ID
```http
POST /api/SQLite/GetById
Content-Type: application/json

{
  "id": 1
}
```

#### Create Setting
```http
POST /api/SQLite/CreateSetting
Content-Type: application/json

{
  "Config_Version": 1,
  "API_KEY": "...",
  "API_SECRET": "...",
  "SYMBOL": "XRPUSDT",
  "PERCEN_BUY": 1.25,
  "PERCEN_SELL": 0.45,
  "BuyAmountUSD": 17.5
}
```

#### Update Setting
```http
POST /api/SQLite/Update
Content-Type: application/json

{
  "Id": 1,
  "Config_Version": 1,
  "API_KEY": "...",
  // ... other fields
}
```

#### Delete Setting
```http
POST /api/SQLite/Delete
Content-Type: application/json

{
  "id": 1
}
```

### Binance APIs

#### Get Spot Report
```http
POST /api/Binace/GetSpotReport
Content-Type: application/json

{
  "ConfigId": 1,  // Optional
  "Period": "1M"  // 1M, 2M, 3M, 1Y, 2Y, 3Y
}
```

#### Open Order
```http
POST /api/Binace/OpenOrder
Content-Type: application/json

{
  "ConfigId": 1,
  "OrderType": "BUY",  // BUY, SELL, BUY_LIMIT_SELL
  "Symbol": "XRPUSDT",
  "Quantity": 10.5,
  "Price": 2.0,  // For limit orders
  "LimitPrice": 2.1  // For BUY_LIMIT_SELL
}
```

#### Cancel Order
```http
POST /api/Binace/CancelOrder
Content-Type: application/json

{
  "ConfigId": 1,
  "Symbol": "XRPUSDT",
  "OrderId": 123456789
}
```

### Discord APIs

#### Log Error
```http
POST /api/Discoed/LogError
Content-Type: application/json

{
  "Webhook1": "...",
  "Webhook2": "...",
  "Message": "Error message",
  "Details": "Error details"
}
```

#### Log Buy
```http
POST /api/Discoed/LogBuy
Content-Type: application/json

{
  "Webhook1": "...",
  "Webhook2": "...",
  "Symbol": "XRPUSDT",
  "Price": 2.0447,
  "Quantity": 8.5,
  "BuyAmount": 17.5,
  "OrderId": "13361757091"
}
```

#### Log Sell
```http
POST /api/Discoed/LogSell
Content-Type: application/json

{
  "Webhook1": "...",
  "Webhook2": "...",
  "Symbol": "XRPUSDT",
  "Price": 2.0539,
  "Quantity": 8.5,
  "ProfitLoss": 0.0092,
  "OrderId": "13361757092"
}
```

#### Log Start
```http
POST /api/Discoed/LogStart
Content-Type: application/json

{
  "Webhook1": "...",
  "Webhook2": "...",
  "Symbol": "XRPUSDT",
  "ConfigId": 1
}
```

#### Log Stop
```http
POST /api/Discoed/LogStop
Content-Type: application/json

{
  "Webhook1": "...",
  "Webhook2": "...",
  "Symbol": "XRPUSDT"
}
```

#### Log Buy Retry
```http
POST /api/Discoed/LogBuyRetry
Content-Type: application/json

{
  "Webhook1": "...",
  "Webhook2": "...",
  "Symbol": "XRPUSDT",
  "Price": 2.0447,
  "RetryCount": 1,
  "Reason": "Retrying after failure"
}
```

#### Log Buy Not Success
```http
POST /api/Discoed/LogBuyNotSuccess
Content-Type: application/json

{
  "Webhook1": "...",
  "Webhook2": "...",
  "Symbol": "XRPUSDT",
  "Error": "Insufficient balance",
  "RetryCount": 0
}
```

---

## 🗄️ โครงสร้างฐานข้อมูล

### Table: db_setting

| Column | Type | Description |
|--------|------|-------------|
| id | int | Primary Key |
| Config_Version | int | เวอร์ชันของ Config |
| API_KEY | string(500) | Binance API Key |
| API_SECRET | string(500) | Binance API Secret |
| DisCord_Hook1 | string(500) | Discord Webhook URL #1 |
| DisCord_Hook2 | string(500) | Discord Webhook URL #2 |
| SYMBOL | string(100) | Trading Symbol (เช่น XRPUSDT) |
| PERCEN_BUY | decimal(18,2) | เปอร์เซ็นต์ลดลงสำหรับซื้อ (เช่น 1.25) |
| PERCEN_SELL | decimal(18,2) | เปอร์เซ็นต์เพิ่มขึ้นสำหรับขาย (เช่น 0.45) |
| BuyAmountUSD | decimal(18,2) | จำนวนเงิน USD ที่ใช้ซื้อแต่ละครั้ง |

### Table: db_Order

| Column | Type | Description |
|--------|------|-------------|
| id | int | Primary Key |
| timestamp | DateTime | เวลาที่สร้าง Order |
| OrderBuyID | string(100) | Binance Order ID สำหรับ Buy |
| PriceBuy | decimal(18,8) | ราคาที่ซื้อ |
| PriceWaitSell | decimal(18,8) | ราคาที่รอขาย (PriceBuy * (1 + PERCEN_SELL/100)) |
| OrderSellID | string(100) | Binance Order ID สำหรับ Sell |
| PriceSellActual | decimal(18,8) | ราคาที่ขายจริง |
| ProfitLoss | decimal(18,8) | กำไร/ขาดทุน (PriceSellActual - PriceBuy) |
| DateBuy | DateTime | วันที่ซื้อ |
| DateSell | DateTime | วันที่ขาย |
| setting_ID | int | Foreign Key ไปยัง db_setting |
| Status | string(50) | WAITING_BUY, BOUGHT, WAITING_SELL, SOLD |
| Symbol | string(100) | Trading Symbol |
| Quantity | decimal(18,8) | จำนวน Coin (calculated) |
| BuyAmountUSD | decimal(18,2) | จำนวนเงิน USD ที่ใช้ซื้อ |
| CoinQuantity | decimal(18,8) | จำนวน Coin ที่ซื้อมาจริง |

---

## 🔔 Discord Logging

ระบบจะส่ง Log ไปยัง Discord Webhook อัตโนมัติสำหรับ:

### ✅ Events ที่ Log

1. **Bot Started** - เมื่อ Bot เริ่มทำงาน
2. **Bot Stopped** - เมื่อ Bot หยุดทำงาน
3. **Buy Order Executed** - เมื่อซื้อสำเร็จ
4. **Sell Order Executed** - เมื่อขายสำเร็จ
5. **Buy Order Failed** - เมื่อซื้อไม่สำเร็จ
6. **Buy Retry** - เมื่อทำการซื้อซ้ำ
7. **Errors** - ข้อผิดพลาดต่างๆ

### 📝 ตัวอย่าง Discord Message

**Buy Order:**
```
🟢 Buy Order Executed
Successfully placed buy order for XRPUSDT

Symbol: XRPUSDT
Price: 2.04470000
Quantity: 8.50000000
Buy Amount (USD): 17.50
Order ID: 13361757091
```

**Sell Order:**
```
🔴 Sell Order Executed
Successfully placed sell order for XRPUSDT

Symbol: XRPUSDT
Sell Price: 2.05390115
Quantity: 8.50000000
Order ID: 13361757092
Profit/Loss: 📈 0.00920115
```

---

## ⚠️ ข้อควรระวัง

1. **ยอด USDT ไม่พอ**: ระบบจะหยุดทำงานทันทีเมื่อยอด USDT ไม่พอ
2. **Duplicate Buy Prevention**: ระบบจะรอ 2 วินาทีระหว่างการซื้อแต่ละครั้ง
3. **Buy Threshold**: ราคาต้องลดลงจากราคาซื้อล่าสุดตาม PERCEN_BUY ก่อนจะซื้อ
4. **Sell Threshold**: ราคาต้องเพิ่มขึ้นถึง PriceWaitSell ถึงจะขาย
5. **Order Cache**: Cache จะ reload อัตโนมัติเมื่อมี Order ขายเหลือ <= 2

---

## 🔧 การแก้ไขปัญหา

### Bot ไม่เริ่มทำงาน
- ตรวจสอบ API_KEY และ API_SECRET ใน Database
- ตรวจสอบ SYMBOL ว่าถูกต้องหรือไม่
- ตรวจสอบ Log ใน Console

### Bot ซื้อซ้ำ
- ตรวจสอบว่า PERCEN_BUY ตั้งค่าถูกต้องหรือไม่
- ตรวจสอบ Last Action Order ใน Database
- ตรวจสอบ Order Cache

### Bot ไม่ขาย
- ตรวจสอบว่า PERCEN_SELL ตั้งค่าถูกต้องหรือไม่
- ตรวจสอบ PriceWaitSell ใน Database
- ตรวจสอบว่า Order Status เป็น WAITING_SELL หรือไม่

### Discord ไม่ได้รับ Log
- ตรวจสอบ Webhook URL ใน Database
- ตรวจสอบว่า Webhook URL ถูกต้องและยังใช้งานได้

---

## 📝 License

MIT License

---

## 👨‍💻 Author

BotGridV1 Development Team

---

**หมายเหตุ**: ระบบนี้ใช้สำหรับการเทรดจริง ควรทดสอบใน Testnet ก่อนใช้งานจริง และควรเข้าใจความเสี่ยงในการเทรด

