using BotGridV1.Models.Login;
using BotGridV1.Models.SQLite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly JwtService _jwtService;
        private readonly ApplicationDbContext _context;

        public LoginController(JwtService jwtService, ApplicationDbContext context)
        {
            _jwtService = jwtService;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Okrun()
        {
           
            return Ok("Okrun");
        }


        [HttpPost]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Username and password are required");
            }

            // Get user from Users table with roles
            var dbUser = await _context.DbUsers
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            // Check if user exists
            if (dbUser == null)
            {
                return Unauthorized("Invalid username or password");
            }

            // Check if account is active
            if (!dbUser.IsActive)
            {
                return Unauthorized("Account is inactive");
            }

            // Check if account is locked
            if (dbUser.IsLocked)
            {
                return Unauthorized("Account is locked. Please contact administrator.");
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

                return Unauthorized("Invalid username or password");
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

            var token = _jwtService.GenerateToken(user);

            return Ok(new LoginResponse
            {
                Token = token,
                ExpireAt = DateTime.Now.AddMinutes(60)
            });
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
        [HttpGet("profile")]
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
    }
}
