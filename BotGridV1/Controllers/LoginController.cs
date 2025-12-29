using BotGridV1.Models.Login;
using BotGridV1.Models.SQLite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly JwtService _jwtService;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public LoginController(JwtService jwtService, ApplicationDbContext context, IConfiguration configuration)
        {
            _jwtService = jwtService;
            _context = context;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> Okrun()
        {
           
            return Ok("Okrun");
        }
        [Authorize]
        [HttpGet]
        public IActionResult TestMe()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = User.Identity?.Name;
            var role = User.FindFirstValue(ClaimTypes.Role);

            return Ok(new
            {
                userId,
                username,
                role,
                message = "Token is valid"
            });
        }


        /// <summary>
        /// Login - Authenticate user and return JWT token
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Login_XX(LoginRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "Username and password are required" });
                }

                // Get user from Users table with roles
                var dbUser = await _context.DbUsers
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Username == request.Username);

                // Check if user exists (use generic message to prevent username enumeration)
                if (dbUser == null)
                {
                    return Unauthorized(new { message = "Invalid username or password" });
                }

                // Check if account is active
                if (!dbUser.IsActive)
                {
                    return Unauthorized(new { message = "Account is inactive. Please contact administrator." });
                }

                // Check if account is locked
                if (dbUser.IsLocked)
                {
                    return Unauthorized(new { message = "Account is locked. Please contact administrator." });
                }

                // Verify password using hashed password
                var passwordValid = PasswordHasher.VerifyPassword(request.Password, dbUser.PasswordHash);

                // Get IP address and user agent for logging
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers["User-Agent"].ToString();

                if (!passwordValid)
                {
                    // Increment failed login count
                    dbUser.FailedLoginCount++;
                    
                    // Lock account after 5 failed attempts
                    if (dbUser.FailedLoginCount >= 5)
                    {
                        dbUser.IsLocked = true;
                    }

                    dbUser.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    // Log failed login attempt
                    var failedLog = new DbUserLoginLog
                    {
                        UserID = dbUser.UserID,
                        Username = dbUser.Username,
                        LoginAt = DateTime.UtcNow,
                        IPAddress = ipAddress,
                        UserAgent = userAgent,
                        IsSuccess = false,
                        FailReason = "Invalid password"
                    };
                    _context.DbUserLoginLogs.Add(failedLog);
                    await _context.SaveChangesAsync();

                    return Unauthorized(new { message = "Invalid username or password" });
                }

                // Successful login - reset failed login count
                dbUser.FailedLoginCount = 0;
                dbUser.LastLoginAt = DateTime.UtcNow;
                dbUser.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Log successful login attempt
                var successLog = new DbUserLoginLog
                {
                    UserID = dbUser.UserID,
                    Username = dbUser.Username,
                    LoginAt = DateTime.UtcNow,
                    IPAddress = ipAddress,
                    UserAgent = userAgent,
                    IsSuccess = true
                };
                _context.DbUserLoginLogs.Add(successLog);
                await _context.SaveChangesAsync();

                // Get user roles
                var roles = dbUser.UserRoles
                    .Select(ur => ur.Role?.RoleName ?? "")
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToList();
                var primaryRole = roles.FirstOrDefault() ?? "User";

                // Create User object for JWT token generation
                var user = new User
                {
                    Id = dbUser.UserID,
                    Username = dbUser.Username,
                    Role = primaryRole
                };

                // Generate JWT token
                var token = _jwtService.GenerateToken(user);

                // Get token expiration time from configuration
                var expireMinutes = int.Parse(_configuration["Jwt:ExpireMinutes"] ?? "60");
                var expireAt = DateTime.UtcNow.AddMinutes(expireMinutes);

                return Ok(new LoginResponse
                {
                    Token = token,
                    RefreshToken = null, // Refresh token not implemented yet
                    ExpireAt = expireAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred during login", error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            try
            {
                // 1️⃣ Validate input
                if (string.IsNullOrWhiteSpace(request.Username) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "Username and password are required" });
                }

                // 2️⃣ Load user + roles
                var dbUser = await _context.DbUsers
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Username == request.Username);

                if (dbUser == null || !dbUser.IsActive)
                {
                    return Unauthorized(new { message = "Invalid username or password" });
                }

                if (dbUser.IsLocked)
                {
                    return Unauthorized(new { message = "Account is locked. Please contact administrator." });
                }

                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var userAgent = Request.Headers["User-Agent"].ToString();

                // 3️⃣ Verify password
                if (!PasswordHasher.VerifyPassword(request.Password, dbUser.PasswordHash))
                {
                    dbUser.FailedLoginCount++;
                    if (dbUser.FailedLoginCount >= 5)
                        dbUser.IsLocked = true;

                    dbUser.UpdatedAt = DateTime.UtcNow;

                    _context.DbUserLoginLogs.Add(new DbUserLoginLog
                    {
                        UserID = dbUser.UserID,
                        Username = dbUser.Username,
                        LoginAt = DateTime.UtcNow,
                        IPAddress = ipAddress,
                        UserAgent = userAgent,
                        IsSuccess = false,
                        FailReason = "Invalid password"
                    });

                    await _context.SaveChangesAsync();
                    return Unauthorized(new { message = "Invalid username or password" });
                }

                // 4️⃣ Login success → reset counters
                dbUser.FailedLoginCount = 0;
                dbUser.LastLoginAt = DateTime.UtcNow;
                dbUser.UpdatedAt = DateTime.UtcNow;

                // 5️⃣ 🔥 Revoke Refresh Token เก่าทั้งหมด (Logout ทุก device)
                await RevokeAll(dbUser.UserID);

                // 6️⃣ Resolve role
                var role = dbUser.UserRoles
                    .Select(ur => ur.Role!.RoleName)
                    .FirstOrDefault() ?? "User";

                // 7️⃣ Generate Access Token (JWT)
                var user = new User
                {
                    Id = dbUser.UserID,
                    Username = dbUser.Username,
                    Role = role
                };
                var accessToken = _jwtService.GenerateToken(user);

                // 8️⃣ Generate Refresh Token
                var refreshToken = Guid.NewGuid().ToString("N");

                var refreshExpireDays =
                    int.Parse(_configuration["Jwt:RefreshExpireDays"] ?? "7");

                _context.DbUserRefreshTokensLogin.Add(new DbUserRefreshTokenLogin
                {
                    UserID = dbUser.UserID,
                    RefreshToken = refreshToken,
                    ExpiredAt = DateTime.UtcNow.AddDays(refreshExpireDays),
                    IsRevoked = false,
                    CreatedAt = DateTime.UtcNow
                });

                // 9️⃣ Expire time (Access Token)
                var expireMinutes =
                    int.Parse(_configuration["Jwt:ExpireMinutes"] ?? "60");

                var expireAt = DateTime.UtcNow.AddMinutes(expireMinutes);

                // 🔟 Log success
                _context.DbUserLoginLogs.Add(new DbUserLoginLog
                {
                    UserID = dbUser.UserID,
                    Username = dbUser.Username,
                    LoginAt = DateTime.UtcNow,
                    IPAddress = ipAddress,
                    UserAgent = userAgent,
                    IsSuccess = true
                });

                await _context.SaveChangesAsync();

                return Ok(new LoginResponse
                {
                    Token = accessToken,
                    RefreshToken = refreshToken,
                    ExpireAt = expireAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "An error occurred during login",
                    error = ex.Message
                });
            }
        }


        /// <summary>
        /// Revoke all refresh tokens for a user
        /// </summary>
        private async Task RevokeAll(int userId)
        {
            var tokens = await _context.DbUserRefreshTokensLogin
                .Where(t => t.UserID == userId && !t.IsRevoked)
                .ToListAsync();

            foreach (var token in tokens)
            {
                token.IsRevoked = true;
            }

            if (tokens.Count > 0)
            {
                await _context.SaveChangesAsync();
            }
        }

        public async Task RevokeByRefreshToken(string refreshToken)
        {
            var token = await _context.DbUserRefreshTokensLogin
                .FirstOrDefaultAsync(t =>
                    t.RefreshToken == refreshToken &&
                    !t.IsRevoked);

            if (token == null) return;

            token.IsRevoked = true;
            await _context.SaveChangesAsync();
        }


        public async Task RevokeAllByUsername(string username)
        {
            var tokens = await _context.DbUserRefreshTokensLogin
                .Where(rt =>
                    !rt.IsRevoked &&
                    _context.DbUsers.Any(u =>
                        u.UserID == rt.UserID &&
                        u.Username == username))
                .ToListAsync();

            foreach (var token in tokens)
            {
                token.IsRevoked = true;
            }

            await _context.SaveChangesAsync();
        }
        /// <summary>
        /// Logout - Revoke all refresh tokens for the current user (all devices)
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                // Get UserID from JWT claims
                var userIdClaim = User.Claims.FirstOrDefault(c => 
                    c.Type == "UserId" || 
                    c.Type == "id" || 
                    c.Type == ClaimTypes.NameIdentifier);

                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    return Unauthorized(new { message = "Invalid token or user ID not found" });
                }

                // Revoke all refresh tokens for this user
                await RevokeAll(userId);

                return Ok(new
                {
                    success = true,
                    message = "Logout successful. All refresh tokens have been revoked."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error during logout",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Logout specific device - Revoke a specific refresh token
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> LogoutDevice(LogoutRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.RefreshToken))
                {
                    return BadRequest(new { message = "RefreshToken is required" });
                }

                await RevokeByRefreshToken(request.RefreshToken);

                return Ok(new
                {
                    success = true,
                    message = "Logout successful (this device only)"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error during logout",
                    error = ex.Message
                });
            }
        }


        /// <summary>
        /// Refresh Access Token using Refresh Token
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> RefreshToken(RefreshTokenRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.RefreshToken))
                    return BadRequest(new { success = false, message = "RefreshToken is required" });

            // 1️⃣ ตรวจ refresh token ใน DB
            var token = await _context.DbUserRefreshTokensLogin
                .FirstOrDefaultAsync(t =>
                    t.RefreshToken == request.RefreshToken &&
                    !t.IsRevoked &&
                    t.ExpiredAt > DateTime.UtcNow);

                if (token == null)
                    return Unauthorized(new { success = false, message = "Invalid or expired refresh token" });

                // 2️⃣ rotate token (ตัดตัวเก่า)
                token.IsRevoked = true;

                // 3️⃣ สร้าง refresh token ใหม่
                var newRefreshToken = Guid.NewGuid().ToString("N");

                var refreshExpireDays =
                    int.Parse(_configuration["Jwt:RefreshExpireDays"] ?? "7");

                _context.DbUserRefreshTokensLogin.Add(new DbUserRefreshTokenLogin
                {
                    UserID = token.UserID,
                    RefreshToken = newRefreshToken,
                    ExpiredAt = DateTime.UtcNow.AddDays(refreshExpireDays),
                    IsRevoked = false,
                    CreatedAt = DateTime.UtcNow
                });

                // 4️⃣ สร้าง access token ใหม่
                var user = await _context.DbUsers
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.UserID == token.UserID);

                if (user == null)
                    return Unauthorized(new { success = false, message = "User not found" });

                // Get user role
                var role = user.UserRoles
                    .Select(ur => ur.Role?.RoleName ?? "")
                    .Where(r => !string.IsNullOrEmpty(r))
                    .FirstOrDefault() ?? "User";

                var userForToken = new User
                {
                    Id = token.UserID,
                    Username = user.Username,
                    Role = role
                };
         
                // Generate JWT token
                var newAccessToken = _jwtService.GenerateToken(userForToken);

                var expireMinutes =
                    int.Parse(_configuration["Jwt:ExpireMinutes"] ?? "60");

                var expireAt = DateTime.UtcNow.AddMinutes(expireMinutes);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    token = newAccessToken,
                    refreshToken = newRefreshToken,
                    expireAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while refreshing token",
                    error = ex.Message
                });
            }
        }


        [HttpPost]
        public async Task<IActionResult> Signup(SignupRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Username and password are required");
            }

            // Check if username already exists in Users table
            var existingUser = await _context.DbUsers
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (existingUser != null)
            {
                return BadRequest("Username already exists");
            }

            // Check if email already exists (if provided)
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var existingEmail = await _context.DbUsers
                    .FirstOrDefaultAsync(u => u.Email == request.Email);

                if (existingEmail != null)
                {
                    return BadRequest("Email already exists");
                }
            }

            // Hash password and generate salt
            var (passwordHash, passwordSalt) = PasswordHasher.HashPasswordWithSalt(request.Password);

            // Create new user in Users table
            var newUser = new DbUser
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                IsActive = true,
                IsLocked = false,
                FailedLoginCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.DbUsers.Add(newUser);
            await _context.SaveChangesAsync();

            return Ok(new SignupResponse
            {
                UserID = newUser.UserID,
                Username = newUser.Username,
                Message = "User registered successfully."
            });
        }

        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            return Ok(new
            {
                Username = User.Identity.Name,
                Claims = User.Claims.Select(c => new { c.Type, c.Value })
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("admin-only")]
        public IActionResult AdminOnly()
        {
            return Ok("Admin Access Granted");
        }

        /// <summary>
        /// Set/Assign a role to a user
        /// </summary>
        [Authorize(Roles = "Administrator,Bot Administrator")]
        [HttpPost]
        public async Task<IActionResult> SetUserRole(SetUserRoleRequest request)
        {
            try
            {
                // Validate input
                if (request.UserID <= 0)
                {
                    return BadRequest(new { success = false, message = "UserID is required and must be greater than 0" });
                }

                if (!request.RoleID.HasValue && string.IsNullOrWhiteSpace(request.RoleCode))
                {
                    return BadRequest(new { success = false, message = "Either RoleID or RoleCode is required" });
                }

                // Check if user exists
                var user = await _context.DbUsers.FindAsync(request.UserID);
                if (user == null)
                {
                    return NotFound(new { success = false, message = $"User with ID {request.UserID} not found" });
                }

                // Get role by ID or Code
                DbRoleLogin? role = null;
                if (request.RoleID.HasValue)
                {
                    role = await _context.DbRolesLogin.FindAsync(request.RoleID.Value);
                }
                else if (!string.IsNullOrWhiteSpace(request.RoleCode))
                {
                    role = await _context.DbRolesLogin
                        .FirstOrDefaultAsync(r => r.RoleCode == request.RoleCode && r.IsActive);
                }

                if (role == null)
                {
                    return NotFound(new { success = false, message = "Role not found or is inactive" });
                }

                // Check if role assignment already exists
                var existingRole = await _context.DbUserRolesLogin
                    .FirstOrDefaultAsync(ur => ur.UserID == request.UserID && ur.RoleID == role.RoleID);

                if (existingRole != null)
                {
                    return BadRequest(new SetUserRoleResponse
                    {
                        Success = false,
                        Message = $"User already has the role '{role.RoleName}'",
                        UserRoleID = existingRole.UserRoleID,
                        UserID = request.UserID,
                        RoleID = role.RoleID,
                        RoleCode = role.RoleCode,
                        RoleName = role.RoleName
                    });
                }

                // Create new role assignment
                var userRole = new DbUserRoleLogin
                {
                    UserID = request.UserID,
                    RoleID = role.RoleID
                };

                _context.DbUserRolesLogin.Add(userRole);
                await _context.SaveChangesAsync();

                return Ok(new SetUserRoleResponse
                {
                    Success = true,
                    Message = $"Role '{role.RoleName}' assigned to user successfully",
                    UserRoleID = userRole.UserRoleID,
                    UserID = request.UserID,
                    RoleID = role.RoleID,
                    RoleCode = role.RoleCode,
                    RoleName = role.RoleName
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while setting user role", error = ex.Message });
            }
        }

        /// <summary>
        /// Remove a role from a user
        /// </summary>
        [Authorize(Roles = "Administrator,Bot Administrator")]
        [HttpPost]
        public async Task<IActionResult> RemoveUserRole(RemoveUserRoleRequest request)
        {
            try
            {
                // Validate input
                if (request.UserID <= 0)
                {
                    return BadRequest(new { success = false, message = "UserID is required and must be greater than 0" });
                }

                if (!request.RoleID.HasValue && string.IsNullOrWhiteSpace(request.RoleCode))
                {
                    return BadRequest(new { success = false, message = "Either RoleID or RoleCode is required" });
                }

                // Check if user exists
                var user = await _context.DbUsers.FindAsync(request.UserID);
                if (user == null)
                {
                    return NotFound(new { success = false, message = $"User with ID {request.UserID} not found" });
                }

                // Get role by ID or Code
                DbRoleLogin? role = null;
                if (request.RoleID.HasValue)
                {
                    role = await _context.DbRolesLogin.FindAsync(request.RoleID.Value);
                }
                else if (!string.IsNullOrWhiteSpace(request.RoleCode))
                {
                    role = await _context.DbRolesLogin
                        .FirstOrDefaultAsync(r => r.RoleCode == request.RoleCode);
                }

                if (role == null)
                {
                    return NotFound(new { success = false, message = "Role not found" });
                }

                // Find and remove role assignment
                var userRole = await _context.DbUserRolesLogin
                    .FirstOrDefaultAsync(ur => ur.UserID == request.UserID && ur.RoleID == role.RoleID);

                if (userRole == null)
                {
                    return NotFound(new { success = false, message = $"User does not have the role '{role.RoleName}'" });
                }

                _context.DbUserRolesLogin.Remove(userRole);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Role '{role.RoleName}' removed from user successfully",
                    userID = request.UserID,
                    roleID = role.RoleID,
                    roleCode = role.RoleCode,
                    roleName = role.RoleName
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while removing user role", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all roles assigned to a user
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetUserRoles(int userID)
        {
            try
            {
                if (userID <= 0)
                {
                    return BadRequest(new { success = false, message = "UserID is required and must be greater than 0" });
                }

                // Get user with roles
                var user = await _context.DbUsers
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.UserID == userID);

                if (user == null)
                {
                    return NotFound(new { success = false, message = $"User with ID {userID} not found" });
                }

                // Map to response
                var roles = user.UserRoles
                    .Where(ur => ur.Role != null)
                    .Select(ur => new UserRoleInfo
                    {
                        RoleID = ur.Role!.RoleID,
                        RoleCode = ur.Role.RoleCode,
                        RoleName = ur.Role.RoleName,
                        IsActive = ur.Role.IsActive
                    })
                    .ToList();

                return Ok(new GetUserRolesResponse
                {
                    Success = true,
                    UserID = user.UserID,
                    Username = user.Username,
                    Roles = roles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while getting user roles", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all available roles
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetRoles()
        {
            try
            {
                // Get all active roles
                var roles = await _context.DbRolesLogin
                    .Where(r => r.IsActive)
                    .OrderBy(r => r.RoleName)
                    .Select(r => new
                    {
                        roleID = r.RoleID,
                        roleCode = r.RoleCode,
                        roleName = r.RoleName,
                        isActive = r.IsActive
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    count = roles.Count,
                    roles = roles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while getting roles", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all roles (including inactive ones)
        /// </summary>
        [Authorize(Roles = "Administrator,Bot Administrator")]
        [HttpGet]
        public async Task<IActionResult> GetAllRoles()
        {
            try
            {
                // Get all roles (including inactive)
                var roles = await _context.DbRolesLogin
                    .OrderBy(r => r.RoleName)
                    .Select(r => new
                    {
                        roleID = r.RoleID,
                        roleCode = r.RoleCode,
                        roleName = r.RoleName,
                        isActive = r.IsActive
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    count = roles.Count,
                    roles = roles
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while getting all roles", error = ex.Message });
            }
        }

        /// <summary>
        /// Get token expiration information - Calculate remaining time in days and hours
        /// </summary>
        [Authorize]
        [HttpGet]
        public IActionResult GetTokenExpiration()
        {
            try
            {
                // Get expiration claim from JWT token
                var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);
                
                if (string.IsNullOrEmpty(expClaim) || !long.TryParse(expClaim, out long expUnix))
                {
                    return BadRequest(new { success = false, message = "Token expiration claim not found" });
                }

                // Convert Unix timestamp to DateTime
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
                var now = DateTime.UtcNow;
                
                // Check if token is expired
                var isExpired = expiresAt <= now;
                
                TimeSpan timeRemaining;
                if (isExpired)
                {
                    timeRemaining = TimeSpan.Zero;
                }
                else
                {
                    timeRemaining = expiresAt - now;
                }

                // Calculate days, hours, minutes
                var days = (int)timeRemaining.TotalDays;
                var hours = timeRemaining.Hours;
                var minutes = timeRemaining.Minutes;
                var totalHours = (int)timeRemaining.TotalHours;
                var totalMinutes = (int)timeRemaining.TotalMinutes;

                // Format time remaining string
                string timeRemainingString;
                if (isExpired)
                {
                    timeRemainingString = "Token has expired";
                }
                else if (days > 0)
                {
                    timeRemainingString = $"{days} day(s), {hours} hour(s), {minutes} minute(s)";
                }
                else if (hours > 0)
                {
                    timeRemainingString = $"{hours} hour(s), {minutes} minute(s)";
                }
                else
                {
                    timeRemainingString = $"{minutes} minute(s)";
                }

                return Ok(new TokenExpirationResponse
                {
                    Success = true,
                    ExpiresAt = expiresAt,
                    Days = days,
                    Hours = hours,
                    Minutes = minutes,
                    TotalHours = totalHours,
                    TotalMinutes = totalMinutes,
                    IsExpired = isExpired,
                    TimeRemaining = timeRemainingString
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred while getting token expiration", error = ex.Message });
            }
        }
    }
}
