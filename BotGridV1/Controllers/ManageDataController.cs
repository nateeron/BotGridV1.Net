using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BotGridV1.Models.SQLite;
using Microsoft.Data.Sqlite;
using System.Text;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class ManageDataController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ManageDataController> _logger;

        // List of allowed tables for security
        private static readonly HashSet<string> AllowedTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "db_setting", "db_Order", "db_alert", "UserAuthori", "Roles", "UserRoles", "RefreshTokens"
        };

        public ManageDataController(ApplicationDbContext context, ILogger<ManageDataController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get list of all table names in the database
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTablesList()
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Get all table names from SQLite master table
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                var tableNames = new List<string>();
                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var tableName = reader.GetString(0);
                                tableNames.Add(tableName);
                            }
                        }
                    }
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }

                return Ok(new
                {
                    success = true,
                    count = tableNames.Count,
                    tables = tableNames
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting table list");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get data from a table by table name with optional filtering and pagination
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetTableData(GetTableDataRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.TableName))
                {
                    return BadRequest(new { success = false, message = "Table name is required" });
                }

                // Security: Validate table name to prevent SQL injection
                if (!AllowedTables.Contains(request.TableName))
                {
                    return BadRequest(new { success = false, message = $"Table '{request.TableName}' is not allowed or does not exist" });
                }

                await _context.Database.EnsureCreatedAsync();

                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                // Declare variables outside try block
                var data = new List<Dictionary<string, object>>();
                int totalCount = 0;

                try
                {
                    // Build query with filters and pagination
                    var sqlBuilder = new StringBuilder($"SELECT * FROM \"{request.TableName}\"");

                    var parameters = new List<SqliteParameter>();
                    var paramIndex = 0;

                    // Add WHERE clause if filters are provided
                    if (request.Filters != null && request.Filters.Count > 0)
                    {
                        var whereConditions = new List<string>();
                        foreach (var filter in request.Filters)
                        {
                            paramIndex++;
                            var paramName = $"@p{paramIndex}";
                            whereConditions.Add($"\"{filter.Key}\" = {paramName}");
                            parameters.Add(new SqliteParameter(paramName, filter.Value ?? DBNull.Value));
                        }
                        sqlBuilder.Append(" WHERE ").Append(string.Join(" AND ", whereConditions));
                    }

                    // Get total count
                    var countSql = sqlBuilder.ToString().Replace("SELECT *", "SELECT COUNT(*)");
                    using (var countCommand = connection.CreateCommand())
                    {
                        countCommand.CommandText = countSql;
                        foreach (var param in parameters)
                        {
                            countCommand.Parameters.Add(param);
                        }
                        var countResult = await countCommand.ExecuteScalarAsync();
                        totalCount = Convert.ToInt32(countResult);
                    }

                    // Add pagination
                    if (request.Page.HasValue && request.PageSize.HasValue)
                    {
                        var offset = (request.Page.Value - 1) * request.PageSize.Value;
                        sqlBuilder.Append($" LIMIT {request.PageSize.Value} OFFSET {offset}");
                    }

                    // Execute query
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = sqlBuilder.ToString();
                        foreach (var param in parameters)
                        {
                            command.Parameters.Add(param);
                        }
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var row = new Dictionary<string, object>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var columnName = reader.GetName(i);
                                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    row[columnName] = value ?? DBNull.Value;
                                }
                                data.Add(row);
                            }
                        }
                    }
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }

                return Ok(new GetTableDataResponse
                {
                    Success = true,
                    Data = data,
                    TotalCount = totalCount,
                    Page = request.Page,
                    PageSize = request.PageSize
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting table data");
                return StatusCode(500, new GetTableDataResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Insert data into a table dynamically
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> InsertData(DynamicInsertRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.TableName))
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = "Table name is required"
                    });
                }

                if (request.Data == null || request.Data.Count == 0)
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = "Data is required"
                    });
                }

                // Security: Validate table name
                if (!AllowedTables.Contains(request.TableName))
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = $"Table '{request.TableName}' is not allowed or does not exist"
                    });
                }

                await _context.Database.EnsureCreatedAsync();

                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                int affectedRows = 0;
                try
                {
                    // Build INSERT query
                    var columns = string.Join(", ", request.Data.Keys.Select(k => $"\"{k}\""));
                    var paramNames = string.Join(", ", request.Data.Keys.Select((k, i) => $"@p{i}"));
                    var sql = $"INSERT INTO \"{request.TableName}\" ({columns}) VALUES ({paramNames})";

                    var parameters = request.Data.Select((kvp, i) => new SqliteParameter($"@p{i}", kvp.Value ?? DBNull.Value)).ToList();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = sql;
                        foreach (var param in parameters)
                        {
                            command.Parameters.Add(param);
                        }
                        affectedRows = await command.ExecuteNonQueryAsync();
                    }
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }

                return Ok(new DynamicOperationResponse
                {
                    Success = true,
                    Message = "Data inserted successfully",
                    AffectedRows = affectedRows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting data");
                return StatusCode(500, new DynamicOperationResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Update data in a table dynamically
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateData(DynamicUpdateRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.TableName))
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = "Table name is required"
                    });
                }

                if (request.UpdateData == null || request.UpdateData.Count == 0)
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = "Update data is required"
                    });
                }

                if (request.WhereConditions == null || request.WhereConditions.Count == 0)
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = "Where conditions are required for update operation"
                    });
                }

                // Security: Validate table name
                if (!AllowedTables.Contains(request.TableName))
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = $"Table '{request.TableName}' is not allowed or does not exist"
                    });
                }

                await _context.Database.EnsureCreatedAsync();

                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                int affectedRows = 0;
                try
                {
                    // Build UPDATE query
                    var sqlBuilder = new StringBuilder($"UPDATE \"{request.TableName}\" SET ");
                    var parameters = new List<SqliteParameter>();
                    var paramIndex = 0;

                    // SET clause
                    var setClauses = new List<string>();
                    foreach (var update in request.UpdateData)
                    {
                        paramIndex++;
                        var paramName = $"@p{paramIndex}";
                        setClauses.Add($"\"{update.Key}\" = {paramName}");
                        parameters.Add(new SqliteParameter(paramName, update.Value ?? DBNull.Value));
                    }
                    sqlBuilder.Append(string.Join(", ", setClauses));

                    // WHERE clause
                    sqlBuilder.Append(" WHERE ");
                    var whereClauses = new List<string>();
                    foreach (var condition in request.WhereConditions)
                    {
                        paramIndex++;
                        var paramName = $"@p{paramIndex}";
                        whereClauses.Add($"\"{condition.Key}\" = {paramName}");
                        parameters.Add(new SqliteParameter(paramName, condition.Value ?? DBNull.Value));
                    }
                    sqlBuilder.Append(string.Join(" AND ", whereClauses));

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = sqlBuilder.ToString();
                        foreach (var param in parameters)
                        {
                            command.Parameters.Add(param);
                        }
                        affectedRows = await command.ExecuteNonQueryAsync();
                    }
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }

                return Ok(new DynamicOperationResponse
                {
                    Success = true,
                    Message = "Data updated successfully",
                    AffectedRows = affectedRows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating data");
                return StatusCode(500, new DynamicOperationResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// Delete data from a table dynamically
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteData(DynamicDeleteRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.TableName))
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = "Table name is required"
                    });
                }

                if (request.WhereConditions == null || request.WhereConditions.Count == 0)
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = "Where conditions are required for delete operation"
                    });
                }

                // Security: Validate table name
                if (!AllowedTables.Contains(request.TableName))
                {
                    return BadRequest(new DynamicOperationResponse
                    {
                        Success = false,
                        Message = $"Table '{request.TableName}' is not allowed or does not exist"
                    });
                }

                await _context.Database.EnsureCreatedAsync();

                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                int affectedRows = 0;
                try
                {
                    // Build DELETE query
                    var sqlBuilder = new StringBuilder($"DELETE FROM \"{request.TableName}\" WHERE ");
                    var parameters = new List<SqliteParameter>();
                    var paramIndex = 0;

                    var whereClauses = new List<string>();
                    foreach (var condition in request.WhereConditions)
                    {
                        paramIndex++;
                        var paramName = $"@p{paramIndex}";
                        whereClauses.Add($"\"{condition.Key}\" = {paramName}");
                        parameters.Add(new SqliteParameter(paramName, condition.Value ?? DBNull.Value));
                    }
                    sqlBuilder.Append(string.Join(" AND ", whereClauses));

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = sqlBuilder.ToString();
                        foreach (var param in parameters)
                        {
                            command.Parameters.Add(param);
                        }
                        affectedRows = await command.ExecuteNonQueryAsync();
                    }
                }
                finally
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }

                return Ok(new DynamicOperationResponse
                {
                    Success = true,
                    Message = "Data deleted successfully",
                    AffectedRows = affectedRows
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting data");
                return StatusCode(500, new DynamicOperationResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }
    }
}
