using BotGridV1.Models.SQLite;
using BotGridV1.Models.Login;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotGridV1.Services
{
    public static class UserLoginTableInitializer
    {
        /// <summary>
        /// Check if UserLogin tables exist, create them if they don't
        /// ตรวจสอบว่าตาราง UserLogin มีอยู่หรือไม่ สร้างถ้ายังไม่มี
        /// </summary>
        public static async Task EnsureUserLoginTablesAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("UserLoginTableInitializer");

            try
            {
                await context.Database.EnsureCreatedAsync();

                var connection = context.Database.GetDbConnection();
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
                            logger.LogInformation("Created Users table");
                        }
                    }

                    // Create Roles table if it doesn't exist
                    bool rolesTableCreated = false;
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
                            rolesTableCreated = true;
                            logger.LogInformation("Created Roles table");
                        }
                    }

                    // Insert default roles if Roles table was just created or is empty
                    bool shouldInsertDefaultRoles = false;
                    if (rolesTableCreated)
                    {
                        // Table was just created, insert default roles
                        shouldInsertDefaultRoles = true;
                    }
                    else
                    {
                        // Check if Roles table is empty
                        using (var checkCommand = connection.CreateCommand())
                        {
                            checkCommand.CommandText = "SELECT COUNT(*) FROM Roles";
                            var roleCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                            if (roleCount == 0)
                            {
                                shouldInsertDefaultRoles = true;
                            }
                        }
                    }

                    if (shouldInsertDefaultRoles)
                    {
                        using (var insertCommand = connection.CreateCommand())
                        {
                            insertCommand.CommandText = @"
                                INSERT INTO Roles (RoleCode, RoleName, IsActive)
                                VALUES
                                    ('ADMIN', 'Administrator', 1),
                                    ('TRADER', 'Trader', 1),
                                    ('BOT_ADMIN', 'Bot Administrator', 1),
                                    ('VIEWER', 'Read Only', 1)";
                            await insertCommand.ExecuteNonQueryAsync();
                            logger.LogInformation("Inserted default roles: ADMIN, TRADER, BOT_ADMIN, VIEWER");
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
                            logger.LogInformation("Created UserRoles table");
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
                            logger.LogInformation("Created UserRefreshTokens table");
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
                            logger.LogInformation("Created UserLoginLogs table");
                        }
                    }

                    // Create default admin user if no users exist
                    using (var checkUserCommand = connection.CreateCommand())
                    {
                        checkUserCommand.CommandText = "SELECT COUNT(*) FROM Users";
                        var userCount = Convert.ToInt32(await checkUserCommand.ExecuteScalarAsync());
                        
                        if (userCount == 0)
                        {
                            // Hash password for default admin user
                            var defaultPassword = "123qwe!@#QWE";
                            var (passwordHash, passwordSalt) = PasswordHasher.HashPasswordWithSalt(defaultPassword);

                            // Get ADMIN role ID
                            using (var roleCommand = connection.CreateCommand())
                            {
                                roleCommand.CommandText = "SELECT RoleID FROM Roles WHERE RoleCode = 'ADMIN' AND IsActive = 1 LIMIT 1";
                                var roleResult = await roleCommand.ExecuteScalarAsync();
                                
                                if (roleResult != null)
                                {
                                    var adminRoleID = Convert.ToInt32(roleResult);

                                    // Insert default admin user
                                    using (var insertUserCommand = connection.CreateCommand())
                                    {
                                        insertUserCommand.CommandText = @"
                                            INSERT INTO Users (Username, Email, PasswordHash, PasswordSalt, FullName, IsActive, IsLocked, FailedLoginCount, CreatedAt)
                                            VALUES ('admin', 'admin@example.com', @PasswordHash, @PasswordSalt, 'Default Administrator', 1, 0, 0, datetime('now'))";
                                        
                                        var hashParam = insertUserCommand.CreateParameter();
                                        hashParam.ParameterName = "@PasswordHash";
                                        hashParam.Value = passwordHash;
                                        insertUserCommand.Parameters.Add(hashParam);
                                        
                                        var saltParam = insertUserCommand.CreateParameter();
                                        saltParam.ParameterName = "@PasswordSalt";
                                        saltParam.Value = passwordSalt;
                                        insertUserCommand.Parameters.Add(saltParam);
                                        
                                        await insertUserCommand.ExecuteNonQueryAsync();
                                        
                                        // Get the inserted user ID
                                        insertUserCommand.CommandText = "SELECT last_insert_rowid()";
                                        var userId = Convert.ToInt32(await insertUserCommand.ExecuteScalarAsync());

                                        // Assign ADMIN role to default user
                                        using (var assignRoleCommand = connection.CreateCommand())
                                        {
                                            assignRoleCommand.CommandText = @"
                                                INSERT INTO UserRoles (UserID, RoleID)
                                                VALUES (@UserID, @RoleID)";
                                            
                                            var userIdParam = assignRoleCommand.CreateParameter();
                                            userIdParam.ParameterName = "@UserID";
                                            userIdParam.Value = userId;
                                            assignRoleCommand.Parameters.Add(userIdParam);
                                            
                                            var roleIdParam = assignRoleCommand.CreateParameter();
                                            roleIdParam.ParameterName = "@RoleID";
                                            roleIdParam.Value = adminRoleID;
                                            assignRoleCommand.Parameters.Add(roleIdParam);
                                            
                                            await assignRoleCommand.ExecuteNonQueryAsync();
                                        }

                                        logger.LogInformation("Created default admin user (username: admin, password: 123qwe!@#QWE) with ADMIN role");
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("Cannot create default admin user: ADMIN role not found");
                                }
                            }
                        }
                    }

                    if (createdTables.Count > 0)
                    {
                        logger.LogInformation("UserLogin tables initialization complete. Created {Count} table(s): {Tables}", 
                            createdTables.Count, string.Join(", ", createdTables));
                    }
                    else
                    {
                        logger.LogInformation("All UserLogin tables already exist");
                    }
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
                logger.LogError(ex, "Error checking/creating UserLogin tables");
                throw;
            }
        }
    }
}
