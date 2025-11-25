using Microsoft.AspNetCore.Mvc;
using BotGridV1.Models;
using BotGridV1.Models.SQLite;
using BotGridV1.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class AlertController : ControllerBase
    {
        private readonly AlertLogService _alertLogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AlertController> _logger;

        public AlertController(AlertLogService alertLogService, IServiceProvider serviceProvider, ILogger<AlertController> logger)
        {
            _alertLogService = alertLogService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Get alert logs from database with filtering and pagination, ordered by newest first
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetLogs(AlertLogRequest? request = null)
        {
            try
            {
                request ??= new AlertLogRequest();

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var query = context.DbAlerts.AsQueryable();

                // Filter by type
                if (!string.IsNullOrEmpty(request.Type))
                {
                    query = query.Where(a => a.Type == request.Type);
                }

                // Filter by level
                if (!string.IsNullOrEmpty(request.Level))
                {
                    query = query.Where(a => a.Level == request.Level);
                }

                // Filter by config ID
                if (!string.IsNullOrEmpty(request.ConfigId))
                {
                    query = query.Where(a => a.ConfigId == request.ConfigId);
                }

                // Filter by read status
                if (request.IsRead.HasValue)
                {
                    query = query.Where(a => a.IsRead == request.IsRead.Value);
                }

                // Filter by date range
                if (request.FromDate.HasValue)
                {
                    query = query.Where(a => a.Timestamp >= request.FromDate.Value);
                }

                if (request.ToDate.HasValue)
                {
                    query = query.Where(a => a.Timestamp <= request.ToDate.Value);
                }

                // Order by timestamp descending (newest first)
                query = query.OrderByDescending(a => a.Timestamp);

                var total = await query.CountAsync();

                // Apply pagination
                var dbAlerts = await query
                    .Skip(request.Offset ?? 0)
                    .Take(request.Limit ?? 100)
                    .ToListAsync();

                // Convert DbAlert to AlertLog
                var logs = dbAlerts.Select(a => new AlertLog
                {
                    Id = a.AlertId,
                    Timestamp = a.Timestamp,
                    Type = a.Type,
                    Level = a.Level,
                    Title = a.Title,
                    Message = a.Message,
                    Details = a.Details,
                    Fields = !string.IsNullOrEmpty(a.FieldsJson) 
                        ? JsonSerializer.Deserialize<Dictionary<string, string>>(a.FieldsJson) 
                        : null,
                    Color = a.Color,
                    ConfigId = a.ConfigId,
                    Symbol = a.Symbol,
                    IsRead = a.IsRead,
                    ReadAt = a.ReadAt
                }).ToList();

                var response = new AlertLogResponse
                {
                    Logs = logs,
                    Total = total,
                    Limit = request.Limit ?? 100,
                    Offset = request.Offset ?? 0
                };

                return Ok(new
                {
                    success = true,
                    data = response
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alert logs");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Get alert by ID with read status
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetAlertById([FromBody] req_GetAlertById req)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var dbAlert = await context.DbAlerts
                    .FirstOrDefaultAsync(a => a.AlertId == req.AlertId || a.Id == req.Id);

                if (dbAlert == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Alert not found"
                    });
                }

                var alert = new AlertLog
                {
                    Id = dbAlert.AlertId,
                    Timestamp = dbAlert.Timestamp,
                    Type = dbAlert.Type,
                    Level = dbAlert.Level,
                    Title = dbAlert.Title,
                    Message = dbAlert.Message,
                    Details = dbAlert.Details,
                    Fields = !string.IsNullOrEmpty(dbAlert.FieldsJson)
                        ? JsonSerializer.Deserialize<Dictionary<string, string>>(dbAlert.FieldsJson)
                        : null,
                    Color = dbAlert.Color,
                    ConfigId = dbAlert.ConfigId,
                    Symbol = dbAlert.Symbol,
                    IsRead = dbAlert.IsRead,
                    ReadAt = dbAlert.ReadAt
                };

                return Ok(new
                {
                    success = true,
                    data = alert,
                    isRead = dbAlert.IsRead,
                    readAt = dbAlert.ReadAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alert by ID");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Mark alert as read
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> MarkAsRead([FromBody] req_MarkAlertRead req)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var dbAlert = await context.DbAlerts
                    .FirstOrDefaultAsync(a => a.AlertId == req.AlertId || a.Id == req.Id);

                if (dbAlert == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Alert not found"
                    });
                }

                dbAlert.IsRead = true;
                dbAlert.ReadAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Alert marked as read",
                    alertId = dbAlert.AlertId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking alert as read");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Mark multiple alerts as read
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> MarkMultipleAsRead([FromBody] req_MarkMultipleAlertsRead req)
        {
            try
            {
                if (req.AlertIds == null || req.AlertIds.Count == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Alert IDs are required"
                    });
                }

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var dbAlerts = await context.DbAlerts
                    .Where(a => req.AlertIds.Contains(a.AlertId) || req.AlertIds.Contains(a.Id.ToString()))
                    .ToListAsync();

                if (dbAlerts.Count == 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "No alerts found"
                    });
                }

                var now = DateTime.UtcNow;
                foreach (var alert in dbAlerts)
                {
                    alert.IsRead = true;
                    alert.ReadAt = now;
                }

                await context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"{dbAlerts.Count} alert(s) marked as read",
                    count = dbAlerts.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking multiple alerts as read");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Mark all alerts as read
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> MarkAllAsRead([FromBody] req_MarkAllAlertsRead? req = null)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var query = context.DbAlerts.Where(a => !a.IsRead);

                // Filter by config ID if provided
                if (req != null && !string.IsNullOrEmpty(req.ConfigId))
                {
                    query = query.Where(a => a.ConfigId == req.ConfigId);
                }

                var dbAlerts = await query.ToListAsync();
                var now = DateTime.UtcNow;

                foreach (var alert in dbAlerts)
                {
                    alert.IsRead = true;
                    alert.ReadAt = now;
                }

                await context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"{dbAlerts.Count} alert(s) marked as read",
                    count = dbAlerts.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all alerts as read");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Get unread alert count
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetUnreadCount([FromBody] req_GetUnreadCount? req = null)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var query = context.DbAlerts.Where(a => !a.IsRead);

                // Filter by config ID if provided
                if (req != null && !string.IsNullOrEmpty(req.ConfigId))
                {
                    query = query.Where(a => a.ConfigId == req.ConfigId);
                }

                var count = await query.CountAsync();

                return Ok(new
                {
                    success = true,
                    count = count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unread count");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Get alert log statistics
        /// </summary>
        [HttpPost]
        public IActionResult GetStatistics()
        {
            try
            {
                var stats = _alertLogService.GetStatistics();

                return Ok(new
                {
                    success = true,
                    data = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting alert statistics");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Clear all alert logs from database
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ClearLogs()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                var allAlerts = await context.DbAlerts.ToListAsync();
                context.DbAlerts.RemoveRange(allAlerts);
                await context.SaveChangesAsync();

                // Also clear in-memory logs
                _alertLogService.ClearLogs();

                return Ok(new
                {
                    success = true,
                    message = "All logs cleared successfully",
                    deletedCount = allAlerts.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing alert logs");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }

    // Request models
    public class req_GetAlertById
    {
        public int? Id { get; set; }
        public string? AlertId { get; set; }
    }

    public class req_MarkAlertRead
    {
        public int? Id { get; set; }
        public string? AlertId { get; set; }
    }

    public class req_MarkMultipleAlertsRead
    {
        public List<string> AlertIds { get; set; } = new();
    }

    public class req_MarkAllAlertsRead
    {
        public string? ConfigId { get; set; }
    }

    public class req_GetUnreadCount
    {
        public string? ConfigId { get; set; }
    }
}

