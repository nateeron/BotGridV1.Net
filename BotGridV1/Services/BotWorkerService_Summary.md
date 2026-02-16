# BotWorkerService – สรุปลำดับขั้นตอนการทำงาน และจุดที่ควรปรับแก้

## 1. โครงสร้างภาพรวม

- **BackgroundService** ถูก Start/Stop จาก API (BotWorkerController) ไม่รัน loop เอง
- เมื่อ **Start**: โหลด config จาก DB → Reload order cache → สร้าง Binance WebSocket → Subscribe ราคาแบบ real-time
- ทุกครั้งที่ได้ **price update** จาก WebSocket จะเรียก `ProcessPriceUpdateAsync(price, config)`
- ใน ProcessPriceUpdate: **คำนวณว่าจะซื้อหรือไม่** → ถ้าควรซื้อเรียก `CheckAndBuyAsync` → จากนั้นเรียก `CheckAndSellAsync` (ขายได้ครั้งละ 1 order)

---

## 2. ลำดับขั้นตอนการทำงาน (Flow)

### 2.1 Start (StartAsync)

1. ตรวจสอบว่า bot ยังไม่รัน (`_isRunning`)
2. โหลด config จาก DB (ตาม configId หรือตัวแรก)
3. ตรวจสอบ API_KEY, API_SECRET, SYMBOL
4. เก็บ `_currentConfig` และโหลด order cache ครั้งแรก (`ReloadOrderCacheAsync`)
5. สร้าง `BinanceSocketClient` และ `CancellationTokenSource`
6. Subscribe `SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(symbol, callback)`
7. ตั้ง `_isRunning = true` และส่ง Discord LogStart

### 2.2 Stop (StopAsync)

1. Cancel `_cancellationTokenSource`
2. Dispose WebSocket client
3. ตั้ง `_isRunning = false` ล้าง `_orderCache`, `_waitBuyTime`, `_currentBuyThreshold`, `_isSold_Price`
4. ส่ง Discord LogStop และเคลียร์ `_currentConfig`

### 2.3 แต่ละครั้งที่ได้ Price (ProcessPriceUpdateAsync)

1. **กันซื้อถี่เกิน**: ถ้าเวลาผ่านจาก `_lastBuyTime` น้อยกว่า `_minBuyInterval` (2 วินาที) → return
2. สร้าง scope และได้ `ApplicationDbContext`
3. **Reload cache (เงื่อนไข)**: ถ้า order ที่ WAITING_SELL ใน cache ≤ 2 → `ReloadOrderCacheAsync`
4. ดึง `waitingSellOrders` จาก cache (WAITING_SELL, เรียง PriceWaitSell asc, Take 20)
5. ดึง `lastActionOrder` จาก DB (order ล่าสุดที่มี DateBuy หรือ DateSell เรียงตาม DateSell ?? DateBuy desc)
6. ดึง `openSellOrders` จาก DB (WAITING_SELL มี PriceWaitSell เรียง asc Take 20)
7. **ตัดสินใจว่าจะเช็ก Buy หรือไม่** (`shouldCheckBuy`, `buyThreshold`) ตามสถานะ lastActionOrder:
   - **ไม่มี order**: shouldCheckBuy = true, threshold = null (ซื้อได้ทันที)
   - **lastAction = SOLD**: คำนวณ threshold จาก `CheckBuy_SOLD(PriceSellActual)` + เงื่อนไข RunUp / รอ 5 นาที
   - **lastAction = Buy (ยังไม่ SOLD)**: คำนวณ threshold จาก `CheckBuy_waillsell(PriceBuy)` ราคาต้อง ≤ threshold ถึงจะซื้อ
   - **lastAction = WAITING_SELL**: ใช้ last SOLD order คำนวณ threshold แบบ SOLD; ถ้าไม่มี SOLD จะไม่ซื้อ
8. ถ้า `_pauseBuyDueToInsufficientBalance` → บังคับ shouldCheckBuy = false
9. ถ้า `shouldCheckBuy` → เรียก `CheckAndBuyAsync(context, config, currentPrice, lastActionOrder, openSellOrders)`
10. เรียก `CheckAndSellAsync(context, config, currentPrice)` เสมอ (ขายได้ครั้งละ 1 order)

### 2.4 CheckAndBuyAsync (เมื่อระบบตัดสินใจว่าควรซื้อ)

1. **Lock ซื้อ**: ใน lock อัปเดต `_lastBuyTime` ก่อน (กันซื้อซ้ำ) ถ้าเพิ่งซื้อไปไม่ถึงช่วงห่าง → return
2. โหลด `freshLastActionOrder` จาก DB อีกครั้ง (กัน race)
3. ถ้ามี order ที่สร้างภายใน 5 วินาที และ Status = WAITING_SELL → return (กันซื้อซ้ำ)
4. คำนวณ `threshold` ตามสถานะ freshLastActionOrder (null / SOLD / Buy / WAITING_SELL)
5. เช็ก RunUp: ถ้าราคา > threshold และไม่เข้าเงื่อนไข RunUp → return
6. สร้าง Binance client; ตรวจสอบ BuyAmountUSD
7. **Margin Cross**: ถ้า `config.UseMarginCross` → ดึง margin account; ถ้า Margin Level ≤ 1.4 → ส่ง Discord Margin Alert + Stop bot + return
8. **Spot เท่านั้น**: เช็กยอด USDT; ถ้าไม่พอ → ตั้ง `_pauseBuyDueToInsufficientBalance` + Discord + return (ไม่ Stop)
9. วางคำสั่งซื้อ:
   - **Margin Cross**: `PlaceMarginOrderAsync(..., MARGIN_BUY)` (HTTP signed); ลองซ้ำ 1 ครั้งถ้าล้ม
   - **Spot**: `SpotApi.Trading.PlaceOrderAsync` (Market, quoteQuantity); ลองซ้ำ 1 ครั้ง
10. สร้าง DbOrder (OrderBuyID, PriceBuy, PriceWaitSell = currentPrice + PERCEN_SELL%, CoinQuantity) บันทึก DB
11. อัปเดต cache และส่ง Discord LogBuy

### 2.5 CheckAndSellAsync

1. ใช้ `_sellSemaphore.WaitAsync(0)` กันขายพร้อมกันหลายตัว (ได้แล้วต้อง Release ใน finally)
2. **Margin Cross**: ถ้า `config.UseMarginCross` และ `!_isRunning` → Start bot แล้ว return (รอรอบถัดไปค่อยขาย)
3. กันขายถี่: ถ้าเวลาจาก `_lastSellTime` < _minSellInterval → return
4. เลือก order ที่จะขาย: จาก cache ที่ WAITING_SELL และ currentPrice >= PriceWaitSell เรียง PriceWaitSell asc ตัวแรก; ถ้าไม่มีใน cache → โหลดจาก DB (fallback)
5. โหลด dbOrder จาก DB; ถ้าไม่มีหรือ Status != WAITING_SELL → ลบออกจาก cache (ถ้ามี) แล้ว return
6. คำนวณ `coinQuantityToSell` จาก dbOrder (CoinQuantity ?? Quantity); ถ้า ≤ 0 → ForceMarkOrderAsSold หรือ Discord ตามกรณี
7. ถ้าเป็น **order รอขายตัวสุดท้าย** (waitingSellCount ≤ 1): โหลด balance (Spot) ปรับ quantity ให้ไม่เกินยอดที่มี
8. วางคำสั่งขาย:
   - **Margin Cross**: `PlaceMarginOrderAsync(..., AUTO_REPAY)` ใช้ quantity
   - **Spot**: `SpotApi.Trading.PlaceOrderAsync` Market sell ใช้ quantity
9. ถ้าสั่งขายล้ม: กรณี last order + IsQuantityTooLowError → ForceMarkOrderAsSold; ไม่ใช่แล้ว → Discord error แล้ว return
10. โหลด freshDbOrder อีกครั้ง; ถ้า status เปลี่ยนไปแล้ว (ขายไปแล้ว) → Discord conflict แล้ว return
11. อัปเดต freshDbOrder (OrderSellID, PriceSellActual, DateSell, Status=SOLD, ProfitLoss); SaveChanges
12. ลบออกจาก cache อัปเดต _lastSellTime; ถ้าเคย pause เพราะยอดไม่พอ → ปลด _pauseBuyDueToInsufficientBalance; ส่ง Discord LogSell

### 2.6 คำนวณจุดซื้อ (Threshold)

- **CheckBuy_SOLD(lastActionSOLD)**: ใช้ order WAITING_SELL ที่มี PriceWaitSell  above/below ค่าขายอ้างอิง หา topPriceSell / bottomPriceSell คำนวณจุดกึ่งกลางและเปอร์เซ็นต์; วน loop ปรับ future_Sell และอัปเดต _isSold_Price จนได้ NexBuy_Final
- **CheckBuy_waillsell(lastActionBuy)**: ดู 3 order ล่าสุดว่ามี SOLD หรือไม่; คำนวณแบบคล้าย SOLD (top/bottom PriceWaitSell) เพื่อหาจุดซื้อถัดไป

### 2.7 Margin

- **PlaceMarginOrderAsync**: สร้าง query (symbol, side, type=MARKET, sideEffectType=MARGIN_BUY หรือ AUTO_REPAY, quoteOrderQty หรือ quantity), sign HMAC-SHA256, POST ไป Binance /sapi/v1/margin/order
- ซื้อ Margin: sideEffectType = MARGIN_BUY
- ขาย Margin: sideEffectType = AUTO_REPAY (ขายแล้วชำระหนี้)

---

## 3. จุดที่ควรสังเกตและประเมินการปรับแก้

### 3.1 ความถูกต้องและความปลอดภัย

| จุด | รายละเอียด | คำแนะนำ |
|-----|------------|----------|
| **Margin Level 1.4** | ค่า 1.4 ตายตัวในโค้ด | พิจารณาออกเป็น config (เช่น MinMarginLevelForBuy) หรือใส่ใน DbSetting |
| **RefreshConfigFromDb** | หลัง SwitchTradingMode จะ refresh แค่ config ที่ส่งมา | ถ้า bot รันด้วย config อื่น ต้องให้ controller ส่ง configId ที่ตรงกับ _currentConfig.Id |
| **Margin เมื่อไม่มี IHttpClientFactory** | ถ้า DI ไม่ส่ง IHttpClientFactory มา PlaceMarginOrder จะ fail | ตรวจสอบ Program.cs / DI ว่า BotWorkerService ได้รับ IHttpClientFactory จริงเมื่อใช้ Margin |
| **StopAsync recursion** | `public override async Task StopAsync(CancellationToken)` เรียก `await StopAsync()` (ไม่มี argument) ซึ่งเป็น method เดิม อาจเกิด stack / infinite loop ได้ในบางกรณี | เปลี่ยนเป็นเรียก logic stop แยก (เช่น private DoStopAsync()) หรือให้ override เรียก base แล้วเรียก internal stop แค่ครั้งเดียว |

### 3.2 Race / Lock

| จุด | รายละเอียด | คำแนะนำ |
|-----|------------|----------|
| **_lastBuyTime** | อัปเดตก่อนวาง order จริง ถ้าวาง order ล้มเหลวเวลาไม่ถูกรีเซ็ต | ถ้าต้องการให้ “ซื้อไม่สำเร็จแล้วลองใหม่ได้เร็ว” อาจพิจารณาย้ายการอัปเดต _lastBuyTime ไปหลัง place order สำเร็จ (แลกกับความเสี่ยงซื้อซ้ำถ้า API ช้า) |
| **_orderCache กับ DB** | มีทั้ง cache และ fallback อ่าน DB โดยตรง อาจมีช่วงที่ cache กับ DB ไม่ตรง | พิจารณาให้มี “แหล่งความจริง” เดียว (เช่น อ่าน DB แล้วอัปเดต cache ทุกครั้ง) หรือกำหนดชัดว่า cache ใช้เมื่อไหร่และเมื่อไหร่ต้อง reload |
| **ReloadOrderCacheAsync** | เงื่อนไข reload คือ WAITING_SELL ใน cache ≤ 2 | อาจทำให้ reload บ่อยเมื่อมี order น้อย; ถ้า order เยอะ cache อาจไม่ถูก reload เมื่อมี order ถูกขายไปแล้ว (ลบจาก DB) จนกว่าจะเหลือ ≤ 2 ใน cache |

### 3.3 Margin / Balance

| จุด | รายละเอียด | คำแนะนำ |
|-----|------------|----------|
| **Sell balance (Margin)** | ตอนขาย Margin ยังใช้ `restClient.SpotApi.Account.GetAccountInfoAsync()` สำหรับ last order เพื่อปรับ quantity | ในโหมด Margin ควรใช้ margin account / cross margin balance แทน spot balance เพื่อความถูกต้อง |
| **Margin buy ไม่เช็กยอด** | โหมด Margin Cross ไม่เช็ก USDT ก่อนซื้อ (ใช้ margin ได้) | สอดคล้องกับดีไซน์; แนะนำให้มี alert หรือ log เมื่อ margin level ต่ำ (เช่น น้อยกว่า 2.0) เพื่อเตือนล่วงหน้า |

### 3.4 Error / Edge case

| จุด | รายละเอียด | คำแนะนำ |
|-----|------------|----------|
| **PlaceMarginOrderAsync ล้ม** | คืน (false, 0, 0, 0, body) ไม่แยกประเภท error | พิจารณา parse error code จาก Binance (เช่น -2010, -1111) เพื่อ retry / alert / log แยกประเภท |
| **CheckBuy_SOLD / CheckBuy_waillsell** | ใช้ `.AsEnumerable()` แล้ว OrderBy ใน memory | ข้อมูลเยอะอาจกิน memory; ถ้า DB รองรับ decimal ORDER BY ได้ควรย้ายไปที่ DB |
| **test = false** | flag ในฟิลด์ `test` ใช้ปิดการวาง order จริง | ควรย้ายเป็น config หรือ environment (เช่น TestMode) ไม่ควร hardcode ใน service |
| **ReloadOrderCacheAsync ล้ม** | catch แล้วไม่ log (silent failure) | อย่างน้อย log warning เพื่อ debug ได้เมื่อ cache ผิดปกติ |

### 3.5 โครงสร้างและบำรุงรักษา

| จุด | รายละเอียด | คำแนะนำ |
|-----|------------|----------|
| **ProcessPriceUpdateAsync ยาว** | มีทั้งโหลด cache, DB, คำนวณ threshold หลายแบบ, เรียก Buy/Sell ใน method เดียว | แยกเป็น private method ตามหน้าที่ (เช่น DetermineBuyThreshold, ShouldCheckBuy, ReloadCacheIfNeeded) จะอ่านและเทสง่ายขึ้น |
| **CheckBuy_SOLD / CheckBuy_waillsell** | logic ค่อนข้างซับซ้อน มี while loop และ state _isSold_Price | เพิ่ม comment สรุปสูตรและตัวอย่างตัวเลข หรือแยกเป็น strategy/helper class พร้อม unit test |
| **Magic number** | 5 วินาที (recent order), 5 นาที (wait buy), 2 วินาที (min buy interval), 1 วินาที (min sell interval), 20 (take orders) | ย้ายเป็น constants หรือ config เพื่อปรับพฤติกรรมได้โดยไม่แก้โค้ด |

---

## 4. สรุปสั้นๆ

- **Flow หลัก**: Start → WebSocket price → ProcessPriceUpdate → (ควรซื้อไหม → CheckAndBuy) → CheckAndSell (ครั้งละ 1 order)
- **โหมด Spot**: เช็กยอด USDT ก่อนซื้อ; ยอดไม่พอ = pause buy (ไม่ Stop bot)
- **โหมด Margin Cross**: เช็ก Margin Level > 1.4 ก่อนซื้อ; ไม่ถึง = Stop + Alert; ขายใช้ AUTO_REPAY; ถ้า bot Stop อยู่จะ Start ก่อนแล้วค่อยขายในรอบถัดไป
- **จุดที่ควรแก้ก่อน**: (1) override StopAsync กัน recursion (2) ตรวจสอบ DI สำหรับ IHttpClientFactory เมื่อใช้ Margin (3) พิจารณาใช้ margin account balance ตอนขายโหมด Margin (4) ย้าย magic numbers และ test flag เป็น config/constants
