using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BotGridV1.Models.SQLite;

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

        /// <summary>
        /// Check if database and table exist, create if they don't
        /// </summary>
        [HttpPost("check-database")]
        public async Task<IActionResult> CheckAndCreateDatabase()
        {
            try
            {
                // Ensure database is created
                await _context.Database.EnsureCreatedAsync();

                // Check if table exists and has data
                var exists = await _context.DbSettings.AnyAsync();
                
                return Ok(new
                {
                    success = true,
                    databaseExists = true,
                    tableExists = true,
                    hasData = exists,
                    message = exists ? "Database and table exist with data" : "Database and table created successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/creating database");
                return StatusCode(500, new { success = false, message = ex.Message });
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
    }
}
