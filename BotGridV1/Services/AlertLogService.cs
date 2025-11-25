using BotGridV1.Models;
using BotGridV1.Models.SQLite;
using Microsoft.AspNetCore.SignalR;
using BotGridV1.Hubs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BotGridV1.Services
{
    /// <summary>
    /// Service to manage and broadcast alert logs
    /// </summary>
    public class AlertLogService
    {
        private readonly IHubContext<AlertHub> _hubContext;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<AlertLog> _logs = new();
        private readonly object _lockObject = new();
        private readonly int _maxLogs = 1000; // Keep last 1000 logs in memory
        private const int _maxDbRows = 200; // Keep last 200 rows in database

        public AlertLogService(IHubContext<AlertHub> hubContext, IServiceProvider serviceProvider)
        {
            _hubContext = hubContext;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Add a log and broadcast to SignalR clients, save to database
        /// </summary>
        public async Task AddLogAsync(AlertLog log)
        {
            lock (_lockObject)
            {
                _logs.Add(log);
                
                // Keep only last N logs
                if (_logs.Count > _maxLogs)
                {
                    _logs.RemoveAt(0);
                }
            }

            // Save to database
            await SaveToDatabaseAsync(log);

            // Broadcast to all clients
            await _hubContext.Clients.All.SendAsync("NewAlert", log);

            // Also broadcast to specific config group if ConfigId is provided
            if (!string.IsNullOrEmpty(log.ConfigId))
            {
                await _hubContext.Clients.Group($"alerts_{log.ConfigId}").SendAsync("NewAlert", log);
            }
        }

        /// <summary>
        /// Save alert to database and maintain 200 row limit
        /// </summary>
        private async Task SaveToDatabaseAsync(AlertLog log)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync();

                // Convert AlertLog to DbAlert
                var dbAlert = new DbAlert
                {
                    AlertId = log.Id,
                    Timestamp = log.Timestamp,
                    Type = log.Type,
                    Level = log.Level,
                    Title = log.Title,
                    Message = log.Message,
                    Details = log.Details,
                    FieldsJson = log.Fields != null ? JsonSerializer.Serialize(log.Fields) : null,
                    Color = log.Color,
                    ConfigId = log.ConfigId,
                    Symbol = log.Symbol,
                    IsRead = false
                };

                context.DbAlerts.Add(dbAlert);
                await context.SaveChangesAsync();

                // Check if we exceed 200 rows, delete oldest
                var count = await context.DbAlerts.CountAsync();
                if (count > _maxDbRows)
                {
                    var toDelete = await context.DbAlerts
                        .OrderBy(a => a.Timestamp)
                        .Take(count - _maxDbRows)
                        .ToListAsync();

                    context.DbAlerts.RemoveRange(toDelete);
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw - we don't want to break the alert system
                // Could use a logger here if available
                Console.WriteLine($"Error saving alert to database: {ex.Message}");
            }
        }

        /// <summary>
        /// Get logs with filtering
        /// </summary>
        public AlertLogResponse GetLogs(AlertLogRequest request)
        {
            lock (_lockObject)
            {
                var query = _logs.AsEnumerable();

                // Filter by type
                if (!string.IsNullOrEmpty(request.Type))
                {
                    query = query.Where(l => l.Type.Equals(request.Type, StringComparison.OrdinalIgnoreCase));
                }

                // Filter by level
                if (!string.IsNullOrEmpty(request.Level))
                {
                    query = query.Where(l => l.Level.Equals(request.Level, StringComparison.OrdinalIgnoreCase));
                }

                // Filter by config ID
                if (!string.IsNullOrEmpty(request.ConfigId))
                {
                    query = query.Where(l => l.ConfigId == request.ConfigId);
                }

                // Filter by date range
                if (request.FromDate.HasValue)
                {
                    query = query.Where(l => l.Timestamp >= request.FromDate.Value);
                }

                if (request.ToDate.HasValue)
                {
                    query = query.Where(l => l.Timestamp <= request.ToDate.Value);
                }

                // Order by timestamp descending
                query = query.OrderByDescending(l => l.Timestamp);

                var total = query.Count();

                // Apply pagination
                var logs = query
                    .Skip(request.Offset ?? 0)
                    .Take(request.Limit ?? 100)
                    .ToList();

                return new AlertLogResponse
                {
                    Logs = logs,
                    Total = total,
                    Limit = request.Limit ?? 100,
                    Offset = request.Offset ?? 0
                };
            }
        }

        /// <summary>
        /// Clear all logs
        /// </summary>
        public void ClearLogs()
        {
            lock (_lockObject)
            {
                _logs.Clear();
            }
        }

        /// <summary>
        /// Get log statistics
        /// </summary>
        public Dictionary<string, int> GetStatistics()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, int>
                {
                    { "Total", _logs.Count },
                    { "Errors", _logs.Count(l => l.Level == "Error") },
                    { "Warnings", _logs.Count(l => l.Level == "Warning") },
                    { "Information", _logs.Count(l => l.Level == "Information") },
                    { "Discord", _logs.Count(l => l.Type == "DISCORD") },
                    { "Buy", _logs.Count(l => l.Type == "BUY") },
                    { "Sell", _logs.Count(l => l.Type == "SELL") }
                };
            }
        }
    }
}

