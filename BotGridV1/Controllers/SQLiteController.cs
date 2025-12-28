using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BotGridV1.Models.SQLite;
using BotGridV1.Services;
using System.Text;
using System.Text.Json;
using System.IO;
using System;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class SQLiteController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SQLiteController> _logger;

        public SQLiteController(ApplicationDbContext context, ILogger<SQLiteController> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region setting

        /// <summary>
        /// Check if database and table exist, create if they don't
        /// ตรวจสอบว่าฐานข้อมูลและตารางมีอยู่หรือไม่ สร้างถ้ายังไม่มี
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CheckAndCreateDatabase()
        {
            try
            {
                // Ensure database is created
                // สร้างฐานข้อมูลถ้ายังไม่มี
                await _context.Database.EnsureCreatedAsync();

                // Seed default data from JSON file if no settings exist
                // เติมข้อมูลตั้งต้นจากไฟล์ JSON หากยังไม่มี settings
                var basePath = Directory.GetCurrentDirectory();
                await DefaultDataSeeder.EnsureDefaultSettingAsync(_context, _logger, basePath);

                // Check if table exists and has data
                // ตรวจสอบว่าตารางมีอยู่และมีข้อมูลหรือไม่
                var hasSettings = await _context.DbSettings.AnyAsync();
                var hasOrders = await _context.DbOrders.AnyAsync();
                
                // Get table counts
                // นับจำนวนข้อมูลในตาราง
                var settingsCount = await _context.DbSettings.CountAsync();
                var ordersCount = await _context.DbOrders.CountAsync();
                var dataSource = _context.Database.GetDbConnection().DataSource ?? "unknown";
                var fullPath = Path.IsPathRooted(dataSource)
                    ? dataSource
                    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dataSource));

                _logger.LogInformation("SQLite database resolved path: {DatabasePath}", fullPath);

                return Ok(new
                {
                    success = true,
                    databaseExists = true,
                    databasePath = fullPath,
                    tables = new
                    {
                        db_setting = new
                        {
                            exists = true,
                            hasData = hasSettings,
                            count = settingsCount
                        },
                        db_Order = new
                        {
                            exists = true,
                            hasData = hasOrders,
                            count = ordersCount
                        }
                    },
                    message = hasSettings || hasOrders 
                        ? $"Database and tables exist. Settings: {settingsCount}, Orders: {ordersCount}" 
                        : "Database and tables created successfully (no data yet)"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/creating database");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }

        /// <summary>
        /// Get all settings
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DbSetting>>> GetAll()
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();
                var settings = await _context.DbSettings.ToListAsync();
                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all settings");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

       


        /// <summary>
        /// Get setting by ID
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<DbSetting>> GetById(req_GetById req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();
                var setting = await _context.DbSettings.FindAsync(req.id);

                if (setting == null)
                {
                    return NotFound(new { success = false, message = $"Setting with ID {req.id} not found" });
                }

                return Ok(setting);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting setting by ID");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Create a new setting
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<DbSetting>> CreateSetting(DbSetting setting)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _context.DbSettings.Add(setting);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetById), new { id = setting.Id }, setting);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating setting");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing setting
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Update(DbSetting setting)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var existingSetting = await _context.DbSettings.FindAsync(setting.Id);
                if (existingSetting == null)
                {
                    return NotFound(new { success = false, message = $"Setting with ID {setting.Id} not found" });
                }

                // Update properties
                existingSetting.Config_Version = setting.Config_Version;
                existingSetting.API_KEY = setting.API_KEY;
                existingSetting.API_SECRET = setting.API_SECRET;
                existingSetting.DisCord_Hook1 = setting.DisCord_Hook1;
                existingSetting.DisCord_Hook2 = setting.DisCord_Hook2;
                existingSetting.SYMBOL = setting.SYMBOL;
                existingSetting.PERCEN_BUY = setting.PERCEN_BUY;
                existingSetting.PERCEN_SELL = setting.PERCEN_SELL;
                existingSetting.BuyAmountUSD = setting.BuyAmountUSD;

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Setting updated successfully", data = existingSetting });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await SettingExists(setting.Id))
                {
                    return NotFound(new { success = false, message = $"Setting with ID {setting.Id} not found" });
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating setting");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Delete a setting
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Delete(req_GetById req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                var setting = await _context.DbSettings.FindAsync(req.id);
                if (setting == null)
                {
                    return NotFound(new { success = false, message = $"Setting with ID {req.id} not found" });
                }

                _context.DbSettings.Remove(setting);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Setting deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting setting");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        private async Task<bool> SettingExists(int id)
        {
            return await _context.DbSettings.AnyAsync(e => e.Id == id);
        }
        #endregion
        #region Order APIs

        /// <summary>
        /// Get all orders
        /// ดึงข้อมูล Order ทั้งหมด
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetOrders()
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();
                //var ordersss = await _context.DbOrders
                //    .OrderByDescending(o => o.Id)
                //    .ToListAsync();
                var orders = await _context.DbOrders
                    .Where(o =>  (o.DateBuy != null || o.DateSell != null))
                    .OrderByDescending(o => o.DateSell ?? o.DateBuy)
                    .ToListAsync();
                return Ok(new
                {
                    success = true,
                    count = orders.Count,
                    data = orders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all orders");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get orders by page with filtering
        /// ดึงข้อมูล Order แบบแบ่งหน้า พร้อมการกรอง
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetOrdersByPage(req_GetOrdersByPage req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Validate input
                if (req.page < 1) req.page = 1;
                if (req.pageSize < 1) req.pageSize = 50;
                if (req.pageSize > 1000) req.pageSize = 1000; // Limit max page size

                // Build query - start with orders that have DateBuy or DateSell
                var query = _context.DbOrders
                    .Where(o => (o.DateBuy != null || o.DateSell != null));

                // Apply filter based on Status
                var filterUpper = !string.IsNullOrEmpty(req.filter) ? req.filter.ToUpper() : "ALL";
                if (filterUpper != "ALL")
                {
                    if (filterUpper == "WAITING_SELL")
                    {
                        query = query.Where(o => o.Status == "WAITING_SELL");
                    }
                    else if (filterUpper == "SOLD")
                    {
                        query = query.Where(o => o.Status == "SOLD");
                    }
                }

                // Get total count before pagination
                var total = await query.CountAsync();

                // Apply ordering based on filter status
                IQueryable<DbOrder> orderedQuery;
                if (filterUpper == "WAITING_SELL")
                {
                    // Sort by DateBuy descending for WAITING_SELL
                    orderedQuery = query.OrderByDescending(o => o.DateBuy);
                }
                else if (filterUpper == "SOLD")
                {
                    // Sort by DateSell descending for SOLD
                    orderedQuery = query.OrderByDescending(o => o.DateSell);
                }
                else
                {
                    // For "All", use DateSell ?? DateBuy descending
                    orderedQuery = query.OrderByDescending(o => o.DateSell ?? o.DateBuy);
                }

                // Apply pagination
                var orders = await orderedQuery
                    .Skip((req.page - 1) * req.pageSize)
                    .Take(req.pageSize)
                    .ToListAsync();

                return Ok(new
                {
                    page = req.page,
                    pageSize = req.pageSize,
                    total = total,
                    data = orders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting orders by page");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get orders by Setting ID
        /// ดึงข้อมูล Order ตาม Setting ID
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetOrdersBySettingId(req_GetById req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();
                var orders = await _context.DbOrders
                    .Where(o => o.Setting_ID == req.id)
                    .OrderByDescending(o => o.Id)
                    .ToListAsync();
                
                return Ok(new
                {
                    success = true,
                    settingId = req.id,
                    count = orders.Count,
                    data = orders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting orders by setting ID");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get order by ID
        /// ดึงข้อมูล Order ตาม ID
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetOrderById(req_GetById req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();
                var order = await _context.DbOrders.FindAsync(req.id);

                if (order == null)
                {
                    return NotFound(new { success = false, message = $"Order with ID {req.id} not found" });
                }

                return Ok(new
                {
                    success = true,
                    data = order
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order by ID");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get orders by Status
        /// ดึงข้อมูล Order ตาม Status
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetOrdersByStatus(req_GetOrdersByStatus req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();
                
                var query = _context.DbOrders.AsQueryable();
                
                if (!string.IsNullOrEmpty(req.Status))
                {
                    query = query.Where(o => o.Status == req.Status);
                }
                
                if (req.SettingId.HasValue)
                {
                    query = query.Where(o => o.Setting_ID == req.SettingId.Value);
                }
                
                var orders = await query
                    .OrderByDescending(o => o.Id)
                    .ToListAsync();
                
                return Ok(new
                {
                    success = true,
                    status = req.Status,
                    settingId = req.SettingId,
                    count = orders.Count,
                    data = orders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting orders by status");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing order
        /// อัปเดต Order ที่มีอยู่
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateOrder(DbOrder order)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var existingOrder = await _context.DbOrders.FindAsync(order.Id);
                if (existingOrder == null)
                {
                    return NotFound(new { success = false, message = $"Order with ID {order.Id} not found" });
                }

                // Update properties
                existingOrder.Timestamp = order.Timestamp;
                existingOrder.OrderBuyID = order.OrderBuyID;
                existingOrder.PriceBuy = order.PriceBuy;
                existingOrder.PriceWaitSell = order.PriceWaitSell;
                existingOrder.OrderSellID = order.OrderSellID;
                existingOrder.PriceSellActual = order.PriceSellActual;
                existingOrder.ProfitLoss = order.ProfitLoss;
                existingOrder.DateBuy = order.DateBuy;
                existingOrder.DateSell = order.DateSell;
                existingOrder.Setting_ID = order.Setting_ID;
                existingOrder.Status = order.Status;
                existingOrder.Symbol = order.Symbol;
                existingOrder.Quantity = order.Quantity;
                existingOrder.BuyAmountUSD = order.BuyAmountUSD;
                existingOrder.CoinQuantity = order.CoinQuantity;

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Order updated successfully", data = existingOrder });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await OrderExists(order.Id))
                {
                    return NotFound(new { success = false, message = $"Order with ID {order.Id} not found" });
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Delete an order
        /// ลบ Order
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteOrder(req_GetById req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Check if order exists first
                // ตรวจสอบว่า order มีอยู่หรือไม่ก่อน
                var orderExists = await _context.DbOrders.AnyAsync(o => o.Id == req.id);
                if (!orderExists)
                {
                    return NotFound(new { success = false, message = $"Order with ID {req.id} not found" });
                }

                // Use ExecuteSqlRaw to delete directly (avoids EF tracking issues)
                // ใช้ ExecuteSqlRaw เพื่อลบโดยตรง (หลีกเลี่ยงปัญหา EF tracking)
                var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM db_Order WHERE id = {0}", req.id);

                if (rowsAffected == 0)
                {
                    return NotFound(new { success = false, message = $"Order with ID {req.id} not found or already deleted" });
                }

                return Ok(new 
                { 
                    success = true, 
                    message = "Order deleted successfully", 
                    deletedOrderId = req.id,
                    rowsAffected = rowsAffected
                });
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error deleting order");
                var innerException = dbEx.InnerException?.Message ?? dbEx.Message;
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = "Error deleting order from database",
                    error = innerException,
                    details = dbEx.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }

        /// <summary>
        /// Delete multiple orders by IDs
        /// ลบ Order หลายตัวตาม IDs
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteOrders(req_DeleteOrders req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                if (req.Ids == null || req.Ids.Count == 0)
                {
                    return BadRequest(new { success = false, message = "Order IDs are required" });
                }

                var orders = await _context.DbOrders
                    .Where(o => req.Ids.Contains(o.Id))
                    .ToListAsync();

                if (orders.Count == 0)
                {
                    return NotFound(new { success = false, message = "No orders found with the provided IDs" });
                }

                // Detach entities to avoid tracking issues
                // แยก entities เพื่อหลีกเลี่ยงปัญหา tracking
                foreach (var order in orders)
                {
                    _context.Entry(order).State = EntityState.Detached;
                }
                
                // Re-attach and mark for deletion
                // เชื่อมต่อใหม่และทำเครื่องหมายเพื่อลบ
                _context.DbOrders.AttachRange(orders);
                _context.DbOrders.RemoveRange(orders);
                
                var result = await _context.SaveChangesAsync();

                return Ok(new 
                { 
                    success = true, 
                    message = $"Deleted {orders.Count} order(s) successfully",
                    deletedCount = orders.Count,
                    rowsAffected = result,
                    deletedIds = orders.Select(o => o.Id).ToList()
                });
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error deleting orders");
                var innerException = dbEx.InnerException?.Message ?? dbEx.Message;
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = "Error deleting orders from database",
                    error = innerException,
                    details = dbEx.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting orders");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }

        /// <summary>
        /// Delete orders by Status
        /// ลบ Order ตาม Status
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteOrdersByStatus(req_DeleteOrdersByStatus req)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                if (string.IsNullOrEmpty(req.Status))
                {
                    return BadRequest(new { success = false, message = "Status is required" });
                }

                var query = _context.DbOrders.Where(o => o.Status == req.Status);
                
                if (req.SettingId.HasValue)
                {
                    query = query.Where(o => o.Setting_ID == req.SettingId.Value);
                }

                var orders = await query.ToListAsync();

                if (orders.Count == 0)
                {
                    return NotFound(new { success = false, message = $"No orders found with Status: {req.Status}" });
                }

                // Detach entities to avoid tracking issues
                // แยก entities เพื่อหลีกเลี่ยงปัญหา tracking
                foreach (var order in orders)
                {
                    _context.Entry(order).State = EntityState.Detached;
                }
                
                // Re-attach and mark for deletion
                // เชื่อมต่อใหม่และทำเครื่องหมายเพื่อลบ
                _context.DbOrders.AttachRange(orders);
                _context.DbOrders.RemoveRange(orders);
                
                var result = await _context.SaveChangesAsync();

                return Ok(new 
                { 
                    success = true, 
                    message = $"Deleted {orders.Count} order(s) with Status: {req.Status}",
                    deletedCount = orders.Count,
                    rowsAffected = result,
                    status = req.Status,
                    settingId = req.SettingId
                });
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error deleting orders by status");
                var innerException = dbEx.InnerException?.Message ?? dbEx.Message;
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = "Error deleting orders from database",
                    error = innerException,
                    details = dbEx.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting orders by status");
                return StatusCode(500, new 
                { 
                    success = false, 
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }

        private async Task<bool> OrderExists(int id)
        {
            return await _context.DbOrders.AnyAsync(e => e.Id == id);
        }

        /// <summary>
        /// Recalculate and update ProfitLoss for all SOLD orders
        /// คำนวณและอัปเดต ProfitLoss สำหรับ order ที่มี Status = "SOLD"
        /// New formula: ProfitLoss_USDT = (PriceSellActual - PriceBuy) × CoinQuantity
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> RecalculateProfitLossForSoldOrders()
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get all orders with Status = "SOLD"
                var soldOrders = await _context.DbOrders
                    .Where(o => o.Status == "SOLD")
                    .ToListAsync();

                if (soldOrders.Count == 0)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "No SOLD orders found to update",
                        updatedCount = 0,
                        skippedCount = 0
                    });
                }

                int updatedCount = 0;
                int skippedCount = 0;

                foreach (var order in soldOrders)
                {
                    // Skip if required fields are missing
                    if (!order.PriceBuy.HasValue || !order.PriceSellActual.HasValue)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Get CoinQuantity (prefer CoinQuantity, fallback to Quantity)
                    var coinQuantity = order.CoinQuantity ?? order.Quantity ?? 0;

                    if (coinQuantity <= 0)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Calculate new ProfitLoss: (PriceSellActual - PriceBuy) × CoinQuantity
                    var newProfitLoss = (order.PriceSellActual.Value - order.PriceBuy.Value) * coinQuantity;

                    // Update ProfitLoss
                    order.ProfitLoss = newProfitLoss;
                    updatedCount++;
                }

                // Save all changes
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Successfully recalculated ProfitLoss for {updatedCount} order(s)",
                    updatedCount = updatedCount,
                    skippedCount = skippedCount,
                    totalSoldOrders = soldOrders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating ProfitLoss for SOLD orders");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }

        #endregion

        #region Backup APIs

        /// <summary>
        /// Export the entire SQLite database (settings + orders) as a JSON backup file.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> BackupExport([FromQuery] bool download = true)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                var settings = await _context.DbSettings
                    .AsNoTracking()
                    .OrderBy(s => s.Id)
                    .ToListAsync();

                var orders = await _context.DbOrders
                    .AsNoTracking()
                    .OrderBy(o => o.Id)
                    .ToListAsync();

                var backup = new DatabaseBackupDto
                {
                    ExportedAtUtc = DateTime.UtcNow,
                    Settings = settings,
                    Orders = orders
                };

                if (!download)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Backup generated successfully",
                        counts = new { settings = settings.Count, orders = orders.Count },
                        data = backup
                    });
                }

                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var fileName = $"botgrid-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                return File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting database backup");
                return StatusCode(500, new { success = false, message = ex.Message, error = ex.ToString() });
            }
        }

        /// <summary>
        /// Import a database backup (settings + orders) from an uploaded JSON file or raw JSON string.
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB
        public async Task<IActionResult> BackupImport([FromForm] BackupImportUploadRequest request)
        {
            try
            {
                if ((request.BackupFile == null || request.BackupFile.Length == 0) && string.IsNullOrWhiteSpace(request.BackupJson))
                {
                    return BadRequest(new { success = false, message = "Backup file or JSON payload is required" });
                }

                string? jsonPayload = request.BackupJson;

                if (request.BackupFile != null && request.BackupFile.Length > 0)
                {
                    using var reader = new StreamReader(request.BackupFile.OpenReadStream(), Encoding.UTF8);
                    jsonPayload = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(jsonPayload))
                {
                    return BadRequest(new { success = false, message = "Backup payload is empty" });
                }

                var backup = JsonSerializer.Deserialize<DatabaseBackupDto>(jsonPayload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (backup == null)
                {
                    return BadRequest(new { success = false, message = "Unable to parse backup data" });
                }

                return await ImportBackupAsync(backup, request.ReplaceExisting);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing database backup");
                return StatusCode(500, new { success = false, message = ex.Message, error = ex.ToString() });
            }
        }

        private async Task<IActionResult> ImportBackupAsync(DatabaseBackupDto backup, bool replaceExisting)
        {
            await _context.Database.EnsureCreatedAsync();

            using var transaction = await _context.Database.BeginTransactionAsync();

            if (replaceExisting)
            {
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM db_Order");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM db_setting");
            }

            var backupSettings = backup.Settings ?? new List<DbSetting>();
            var backupOrders = backup.Orders ?? new List<DbOrder>();

            var settingsToInsert = backupSettings
                .OrderBy(s => s.Id)
                .Select(s => new DbSetting
                {
                    Config_Version = s.Config_Version,
                    API_KEY = s.API_KEY,
                    API_SECRET = s.API_SECRET,
                    DisCord_Hook1 = s.DisCord_Hook1,
                    DisCord_Hook2 = s.DisCord_Hook2,
                    SYMBOL = s.SYMBOL,
                    PERCEN_BUY = s.PERCEN_BUY,
                    PERCEN_SELL = s.PERCEN_SELL,
                    BuyAmountUSD = s.BuyAmountUSD
                })
                .ToList();

            if (settingsToInsert.Count > 0)
            {
                _context.DbSettings.AddRange(settingsToInsert);
                await _context.SaveChangesAsync();
            }

            var settingIdMap = new Dictionary<int, int>();
            for (int i = 0; i < backupSettings.Count && i < settingsToInsert.Count; i++)
            {
                var originalId = backupSettings[i].Id;
                var newId = settingsToInsert[i].Id;
                settingIdMap[originalId] = newId;
            }

            var ordersToInsert = backupOrders
                .OrderBy(o => o.Id)
                .Where(o => settingIdMap.ContainsKey(o.Setting_ID))
                .Select(o => new DbOrder
                {
                    Timestamp = o.Timestamp,
                    OrderBuyID = o.OrderBuyID,
                    PriceBuy = o.PriceBuy,
                    PriceWaitSell = o.PriceWaitSell,
                    OrderSellID = o.OrderSellID,
                    PriceSellActual = o.PriceSellActual,
                    ProfitLoss = o.ProfitLoss,
                    DateBuy = o.DateBuy,
                    DateSell = o.DateSell,
                    Setting_ID = settingIdMap[o.Setting_ID],
                    Status = o.Status,
                    Symbol = o.Symbol,
                    Quantity = o.Quantity,
                    BuyAmountUSD = o.BuyAmountUSD,
                    CoinQuantity = o.CoinQuantity
                })
                .ToList();

            var skippedOrders = backupOrders.Count - ordersToInsert.Count;

            if (ordersToInsert.Count > 0)
            {
                _context.DbOrders.AddRange(ordersToInsert);
                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            return Ok(new
            {
                success = true,
                message = "Backup imported successfully",
                imported = new
                {
                    settings = settingsToInsert.Count,
                    orders = ordersToInsert.Count,
                    skippedOrders
                },
                replaceExisting
            });
        }

        #endregion

        #region UserLogin Tables

        /// <summary>
        /// Check if UserLogin tables exist, create them if they don't
        /// ตรวจสอบว่าตาราง UserLogin มีอยู่หรือไม่ สร้างถ้ายังไม่มี
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CheckAndCreateUserLoginTables()
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                var createdTables = new List<string>();
                var existingTables = new List<string>();

                try
                {
                    // Check which tables exist
                    using (var checkCommand = connection.CreateCommand())
                    {
                        checkCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Users', 'Roles', 'UserRoles', 'UserRefreshTokens', 'UserLoginLogs')";
                        using (var reader = await checkCommand.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                existingTables.Add(reader.GetString(0));
                            }
                        }
                    }

                    // Create Users table if it doesn't exist
                    if (!existingTables.Contains("Users"))
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                CREATE TABLE Users (
                                    UserID              INTEGER PRIMARY KEY AUTOINCREMENT,
                                    Username            TEXT NOT NULL UNIQUE,
                                    Email               TEXT NULL UNIQUE,
                                    PasswordHash        TEXT NOT NULL,
                                    PasswordSalt        TEXT NOT NULL,
                                    FullName            TEXT NULL,
                                    PhoneNumber         TEXT NULL,
                                    IsActive            INTEGER NOT NULL DEFAULT 1,
                                    IsLocked            INTEGER NOT NULL DEFAULT 0,
                                    FailedLoginCount    INTEGER NOT NULL DEFAULT 0,
                                    LastLoginAt         TEXT NULL,
                                    CreatedAt           TEXT NOT NULL DEFAULT (datetime('now')),
                                    UpdatedAt           TEXT NULL
                                )";
                            await command.ExecuteNonQueryAsync();
                            createdTables.Add("Users");
                        }
                    }

                    // Create Roles table if it doesn't exist
                    if (!existingTables.Contains("Roles"))
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                CREATE TABLE Roles (
                                    RoleID      INTEGER PRIMARY KEY AUTOINCREMENT,
                                    RoleCode    TEXT NOT NULL UNIQUE,
                                    RoleName    TEXT NOT NULL,
                                    IsActive    INTEGER NOT NULL DEFAULT 1
                                )";
                            await command.ExecuteNonQueryAsync();
                            createdTables.Add("Roles");
                        }
                    }

                    // Create UserRoles table if it doesn't exist
                    if (!existingTables.Contains("UserRoles"))
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                CREATE TABLE UserRoles (
                                    UserRoleID  INTEGER PRIMARY KEY AUTOINCREMENT,
                                    UserID      INTEGER NOT NULL,
                                    RoleID      INTEGER NOT NULL,
                                    FOREIGN KEY (UserID) REFERENCES Users(UserID),
                                    FOREIGN KEY (RoleID) REFERENCES Roles(RoleID),
                                    UNIQUE(UserID, RoleID)
                                )";
                            await command.ExecuteNonQueryAsync();
                            createdTables.Add("UserRoles");
                        }
                    }

                    // Create UserRefreshTokens table if it doesn't exist
                    if (!existingTables.Contains("UserRefreshTokens"))
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                CREATE TABLE UserRefreshTokens (
                                    TokenID         INTEGER PRIMARY KEY AUTOINCREMENT,
                                    UserID          INTEGER NOT NULL,
                                    RefreshToken    TEXT NOT NULL,
                                    ExpiredAt       TEXT NOT NULL,
                                    IsRevoked       INTEGER NOT NULL DEFAULT 0,
                                    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now')),
                                    FOREIGN KEY (UserID) REFERENCES Users(UserID)
                                )";
                            await command.ExecuteNonQueryAsync();
                            createdTables.Add("UserRefreshTokens");
                        }
                    }

                    // Create UserLoginLogs table if it doesn't exist
                    if (!existingTables.Contains("UserLoginLogs"))
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                CREATE TABLE UserLoginLogs (
                                    LogID       INTEGER PRIMARY KEY AUTOINCREMENT,
                                    UserID      INTEGER NULL,
                                    Username    TEXT NULL,
                                    LoginAt     TEXT NOT NULL DEFAULT (datetime('now')),
                                    IPAddress   TEXT NULL,
                                    UserAgent   TEXT NULL,
                                    IsSuccess   INTEGER NOT NULL,
                                    FailReason  TEXT NULL,
                                    FOREIGN KEY (UserID) REFERENCES Users(UserID)
                                )";
                            await command.ExecuteNonQueryAsync();
                            createdTables.Add("UserLoginLogs");
                        }
                    }

                    // Verify all tables exist now
                    var allTables = new List<string>();
                    using (var verifyCommand = connection.CreateCommand())
                    {
                        verifyCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Users', 'Roles', 'UserRoles', 'UserRefreshTokens', 'UserLoginLogs') ORDER BY name";
                        using (var reader = await verifyCommand.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                allTables.Add(reader.GetString(0));
                            }
                        }
                    }

                    return Ok(new
                    {
                        success = true,
                        message = createdTables.Count > 0
                            ? $"Created {createdTables.Count} table(s): {string.Join(", ", createdTables)}"
                            : "All UserLogin tables already exist",
                        createdTables = createdTables,
                        existingTables = existingTables,
                        allTables = allTables,
                        totalTables = allTables.Count
                    });
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/creating UserLogin tables");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }

        #endregion
    }

    public class BackupImportUploadRequest
    {
        public bool ReplaceExisting { get; set; } = true;
        public IFormFile? BackupFile { get; set; }
        public string? BackupJson { get; set; }
    }
}
