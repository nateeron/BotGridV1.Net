using JwtAppLogin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using BotGridV1.Services;
using BotGridV1.Interface;
using BotGridV1.Models;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Linq;

namespace BotGridV1.Registration;

[ApiController]
[Route("api/[controller]/[Action]")]
[Produces("application/json")]
public class RegistrationController : ControllerBase
{
    private string _constrings = string.Empty;
    private string _senderEmail = string.Empty;
    private string _passwordEmail = string.Empty;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private FN_SetData _fnSetData;
    private readonly IWebHostEnvironment _environment;
    private static readonly string[] AllowedImageExtensions = new[] { ".png", ".jpg", ".jpeg" };
    private const long MaxImageSizeBytes = 5 * 1024 * 1024; //5MB

    public RegistrationController(IJwtTokenGenerator jwtTokenGenerator, IConfiguration config, FN_SetData fnSetData, IWebHostEnvironment environment)
    {
        _constrings = config.GetConnectionString("DefaultConnection");
        _jwtTokenGenerator = jwtTokenGenerator;
        _fnSetData = fnSetData;
        _environment = environment;
        // Sender's Gmail address and credentials
        _senderEmail = "test@adidigi.com"; // Replace with your Gmail address
        _passwordEmail = "******"; // Replace with your Gmail password

    }

    #region signUp
    // หน้าที่ผู้ใช้กรอกข้อมูลเพื่อลงทะเบียนเข้าระบบ (แล้วระบบจะสร้างบัญชีใหม่ให้) Freelance
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Registration([FromForm] req_Registration req)
    {
        var sid = User.FindFirst("Sid")?.Value;
        string connettion = await _fnSetData.Decode(_constrings);
        string? publicImageUrl = null;
        string? storedImagePath = null;

  

        if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName) || string.IsNullOrWhiteSpace(req.Email))
        {
            _fnSetData.WriteLog("Registration validation failed: First name, last name, and email are required.");
            return BadRequest("First name, last name, and email are required.");
        }

        if (req.ImageFile != null && req.ImageFile.Length > 0)
        {
            var validateResult = ValidateImage(req.ImageFile);
            if (!validateResult.IsValid)
            {
                _fnSetData.WriteLog($"Registration image validation failed: {validateResult.ErrorMessage}");
                return BadRequest(validateResult.ErrorMessage);
            }

            var uploadResult = await SaveImageAsync(req.ImageFile);
            storedImagePath = uploadResult.RelativePath;
            publicImageUrl = uploadResult.PublicUrl;
        }

        // Track if we need to clean up the image on error
        bool imageUploaded = !string.IsNullOrWhiteSpace(storedImagePath);

        #region Call Stored Procedure to Check and Insert
        var fullNameDisplay = $"{req.FirstName} {req.LastName}".Trim();
        var createBy = string.IsNullOrWhiteSpace(fullNameDisplay) ? "API" : fullNameDisplay;
        ResetTokenResult resetTokenResult = null;
        try
        {
             resetTokenResult = _jwtTokenGenerator.GenerateResetToken(req.Email);

        }
        catch (Exception ex)
        {
            string errorMsg = ex?.Message ?? "failed";
            _fnSetData.WriteLog($" failed: {errorMsg}");

        }

        string resetTokenKey = resetTokenResult?.ResetKey;
        string resetTokenCombined = resetTokenResult?.Token ?? string.Empty;
        
        // Convert ExpertiseID string to comma-separated format [1,2,3] if provided
        string expertiseIdsString = null;
        if (!string.IsNullOrWhiteSpace(req.ExpertiseID))
        {
            // If it's already in array format, use it; otherwise format it
            if (req.ExpertiseID.TrimStart().StartsWith("["))
            {
                expertiseIdsString = req.ExpertiseID;
            }
            else
            {
                // Assume comma-separated values and add brackets
                expertiseIdsString = "[" + req.ExpertiseID + "]";
            }
        }

        resp_InsertUserRegistration result = null;

        using (SqlConnection connection = new SqlConnection(connettion))
        {
            try
            {
                await connection.OpenAsync();

                using (SqlCommand command = new SqlCommand("sp_InsertUserRegistration", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    // Add parameters (UserLoginID is auto-generated, not passed as parameter)
                    command.Parameters.AddWithValue("@FirstName", req.FirstName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@LastName", req.LastName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Email", req.Email ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Phone", req.Phone ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Nationality", req.Nationality ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@PathImage", (object?)storedImagePath ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Country", req.Country ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@City", req.City ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Preferren", req.Preferren ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@BankAccount", req.BankAccount ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@BankName", req.BankName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@SWIFTCode", req.SWIFTCode ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@ExpertiseID", expertiseIdsString ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CreateBy", createBy);
                    command.Parameters.AddWithValue("@UpdateBy", createBy);
                    command.Parameters.AddWithValue("@UserPassword", DBNull.Value); // Can be set later if needed
                    command.Parameters.AddWithValue("@UserLogin", req.Email ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@IsActive", true);
                    command.Parameters.AddWithValue("@IsDelete", false);
                    command.Parameters.AddWithValue("@ResetToken", (object?)resetTokenKey ?? DBNull.Value);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        // First result set: Success/Error status
                        if (await reader.ReadAsync())
                        {
                            result = new resp_InsertUserRegistration
                            {
                                Success = reader.GetBoolean(reader.GetOrdinal("Success")),
                                ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage")),
                                UserProfileID = reader.IsDBNull(reader.GetOrdinal("UserProfileID")) ? null : reader.GetInt32(reader.GetOrdinal("UserProfileID")),
                                UserLoginID = reader.IsDBNull(reader.GetOrdinal("UserLoginID")) ? null : reader.GetString(reader.GetOrdinal("UserLoginID"))
                            };
                        }

                        // If not successful, delete uploaded image and return error
                        if (result == null || !result.Success)
                        {
                            // Log the error
                            string errorMsg = result?.ErrorMessage ?? "User registration failed";
                            _fnSetData.WriteLog($"Registration failed: {errorMsg}");
                            
                            // Delete uploaded image file if registration failed
                            if (imageUploaded)
                            {
                                DeleteImageFile(storedImagePath);
                            }

                            return StatusCode(StatusCodes.Status403Forbidden, new
                            {
                                Message = errorMsg,
                                Success = false
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Delete uploaded image file if there was an error
                if (imageUploaded)
                {
                    DeleteImageFile(storedImagePath);
                }

                string errorMessage = $"{DateTime.Now} \n Error: {ex.Message} StackTrace: {ex.StackTrace}";

                if (ex is SqlException sqlEx)
                {
                    foreach (SqlError error in sqlEx.Errors)
                    {
                        errorMessage += $"\n SQL Error Number: {error.Number} - {error.Message}";
                    }
                }
                _fnSetData.WriteLog(errorMessage);

                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
        #endregion

        return StatusCode(StatusCodes.Status200OK, new
        {
            Message = "Success",
            ResetToken = resetTokenCombined
        });
    }
    #endregion

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ResetPassword(req_ResetPassword req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.NewPassword))
        {
            _fnSetData.WriteLog("ResetPassword validation failed: New password is required.");
            return BadRequest("New password is required.");
        }
        var resetKeyClaim = User.FindFirst("reset_key")?.Value;
        if (string.IsNullOrWhiteSpace(resetKeyClaim))
        {
            _fnSetData.WriteLog("ResetPassword failed: reset_key claim not found.");
            return Unauthorized("Reset token not found.");
        }

        var email = User.FindFirst(ClaimTypes.Email)?.Value
                    ?? User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                    ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(email))
        {
            _fnSetData.WriteLog("ResetPassword failed: Email claim not found.");
            return Unauthorized("User email not found.");
        }

        string hashedPassword;
        try
        {
            hashedPassword = _jwtTokenGenerator.HashPassword(req.NewPassword);
        }
        catch (Exception ex)
        {
            _fnSetData.WriteLog($"ResetPassword hashing failed: {ex.Message}");
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to hash password.");
        }

        string connectionString = await _fnSetData.Decode(_constrings);
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            try
            {
                await connection.OpenAsync();

                const string tokenSql = @"
                                        SELECT [ResetToken]
                                        FROM [FMS_API].[dbo].[Registration]
                                        WHERE [UserLogin] = @UserLogin;";

                using (SqlCommand tokenCommand = new SqlCommand(tokenSql, connection))
                {
                    tokenCommand.Parameters.AddWithValue("@UserLogin", email);
                    var dbTokenObj = await tokenCommand.ExecuteScalarAsync();
                    var dbResetToken = dbTokenObj == DBNull.Value ? null : dbTokenObj?.ToString();

                    if (string.IsNullOrWhiteSpace(dbResetToken))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, new
                        {
                            Message = "Reset token not found or already used.",
                            Success = false
                        });
                    }

                    if (!string.Equals(dbResetToken, resetKeyClaim, StringComparison.Ordinal))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, new
                        {
                            Message = "Invalid reset token.",
                            Success = false
                        });
                    }
                }

                string sql = @"
                                UPDATE [FMS_API].[dbo].[Registration]
                                SET [UserPassword] = @UserPassword,
                                    [UpdateDate] = GETDATE(),
                                    [UpdateBy] = @UpdateBy,
                                    [ResetToken] = NULL
                                WHERE [UserLogin] = @UserLogin;
                               ";

                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@UserPassword", hashedPassword);
                    command.Parameters.AddWithValue("@UpdateBy", email);
                    command.Parameters.AddWithValue("@UserLogin", email);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        return Ok(new
                        {
                            Message = "Password reset successfully.",
                            Success = true
                        });
                    }
                }

                return NotFound(new
                {
                    Message = "User not found.",
                    Success = false
                });
            }
            catch (Exception ex)
            {
                string errorMessage = $"{DateTime.Now} \n Error: {ex.Message} StackTrace: {ex.StackTrace}";
                if (ex is SqlException sqlEx)
                {
                    foreach (SqlError error in sqlEx.Errors)
                    {
                        errorMessage += $"\n SQL Error Number: {error.Number} - {error.Message}";
                    }
                }
                _fnSetData.WriteLog(errorMessage);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }

    [HttpPost]
    public async Task<IActionResult> Login(reqLogin req)
    {
        // Early validation
        if (req == null || string.IsNullOrWhiteSpace(req.username) || string.IsNullOrWhiteSpace(req.password))
        {
            return BadRequest(new { Message = "Username and password are required.", Success = false });
        }

        try
        {
            string connectionString = await _fnSetData.Decode(_constrings);
            
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                
                // Single query to get user data
                const string sql = @"
SELECT TOP (1) [UserID], [UserPassword], [UserLogin]
FROM [FMS_API].[dbo].[Registration]
WHERE [UserLogin] = @UserLogin;";

                int userId = 0;
                string userLogin = string.Empty;
                string hashedPassword = null;

                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@UserLogin", req.username);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                        {
                            return Unauthorized(new { Message = "User not found.", Success = false });
                        }

                        // Read all data in one pass
                        int userIDOrdinal = reader.GetOrdinal("UserID");
                        int userPasswordOrdinal = reader.GetOrdinal("UserPassword");
                        int userLoginOrdinal = reader.GetOrdinal("UserLogin");

                        userId = reader.GetInt32(userIDOrdinal);
                        hashedPassword = reader.IsDBNull(userPasswordOrdinal) ? null : reader.GetString(userPasswordOrdinal);
                        userLogin = reader.IsDBNull(userLoginOrdinal) ? req.username : reader.GetString(userLoginOrdinal);
                    }
                }

                // Verify password before updating database
                if (string.IsNullOrWhiteSpace(hashedPassword) || !_jwtTokenGenerator.VerifyPassword(req.password, hashedPassword))
                {
                    return Unauthorized(new { Message = "Password invalid.", Success = false });
                }

                // Generate token version and update in single operation
                string newTokenVersion = GenerateRandomTokenVersion(20);
                const string updateSql = @"
                                        UPDATE [FMS_API].[dbo].[Registration]
                                        SET [Token_version] = @TokenVersion,
                                            [UpdateDate] = GETDATE(),
                                            [UpdateBy] = @UserLogin
                                        WHERE [UserLogin] = @UserLogin;";
                
                using (SqlCommand updateCommand = new SqlCommand(updateSql, connection))
                {
                    updateCommand.Parameters.AddWithValue("@TokenVersion", newTokenVersion);
                    updateCommand.Parameters.AddWithValue("@UserLogin", req.username);
                    await updateCommand.ExecuteNonQueryAsync();
                }

                // Generate JWT token
                var userInfo = new SetRoles
                {
                    users = userLogin,
                    level = 0,
                    UserID = userId.ToString(),
                    expiresMin = 60
                };

                string token = _jwtTokenGenerator.GenerateJWTToken(userInfo, newTokenVersion);

                return Ok(new
                {
                    Message = "Login successful.",
                    Success = true,
                    Token = token
                });
            }
        }
        catch (SqlException sqlEx)
        {
            string errorMessage = $"{DateTime.Now} \n SQL Error: {sqlEx.Message}";
            foreach (SqlError error in sqlEx.Errors)
            {
                errorMessage += $"\n SQL Error Number: {error.Number} - {error.Message}";
            }
            _fnSetData.WriteLog(errorMessage);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Database error occurred.", Success = false });
        }
        catch (Exception ex)
        {
            string errorMessage = $"{DateTime.Now} \n Error: {ex.Message} StackTrace: {ex.StackTrace}";
            _fnSetData.WriteLog(errorMessage);
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred while processing login.", Success = false });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        try
        {
            // Get user email from JWT claims
            var email = User.FindFirst(ClaimTypes.Email)?.Value
                        ?? User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                        ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(email))
            {
                _fnSetData.WriteLog("Logout failed: Email claim not found in token.");
                return Unauthorized(new
                {
                    Message = "User email not found in token.",
                    Success = false
                });
            }

            string connectionString = await _fnSetData.Decode(_constrings);
            
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Generate new random 20-character Token_version
                string newTokenVersion = GenerateRandomTokenVersion(20);
                const string updateSql = @"UPDATE [FMS_API].[dbo].[Registration]
                                            SET [Token_version] = @TokenVersion,
                                                [UpdateDate] = GETDATE(),
                                                [UpdateBy] = @UserLogin
                                            WHERE [UserLogin] = @UserLogin;";

                using (SqlCommand updateCommand = new SqlCommand(updateSql, connection))
                {
                    updateCommand.Parameters.AddWithValue("@TokenVersion", newTokenVersion);
                    updateCommand.Parameters.AddWithValue("@UserLogin", email);
                    int rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        return Ok(new
                        {
                            Message = "Logout successful. Token invalidated.",
                            Success = true
                        });
                    }
                    else
                    {
                        _fnSetData.WriteLog($"Logout failed: User not found in database for email: {email}");
                        return NotFound(new
                        {
                            Message = "User not found in database.",
                            Success = false
                        });
                    }
                }
            }
        }
        catch (SqlException sqlEx)
        {
            string errorMessage = $"{DateTime.Now} \n SQL Error in Logout: {sqlEx.Message}";
            foreach (SqlError error in sqlEx.Errors)
            {
                errorMessage += $"\n SQL Error Number: {error.Number} - {error.Message}";
            }
            _fnSetData.WriteLog(errorMessage);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                Message = "Database error occurred during logout.",
                Success = false
            });
        }
        catch (Exception ex)
        {
            string errorMessage = $"{DateTime.Now} \n Error in Logout: {ex.Message} StackTrace: {ex.StackTrace}";
            _fnSetData.WriteLog(errorMessage);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                Message = "An error occurred while processing logout.",
                Success = false
            });
        }
    }

    /// <summary>
    /// Change user password. Requires old password verification.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] req_ChangePassword req)
    {
        try
        {
            // Get UserLoginID from token
            var sid = User.FindFirst("Sid")?.Value;
            
            if (string.IsNullOrWhiteSpace(sid))
            {
                _fnSetData.WriteLog("ChangePassword: UserLoginID (Sid) not found in token claims.");
                return Unauthorized(new resp_ChangePassword
                {
                    Success = false,
                    Message = "UserLoginID not found in token."
                });
            }

            if (!int.TryParse(sid, out int userLoginID))
            {
                _fnSetData.WriteLog($"ChangePassword: Invalid UserLoginID format: {sid}");
                return BadRequest(new resp_ChangePassword
                {
                    Success = false,
                    Message = "Invalid UserLoginID format."
                });
            }

            // Validate input
            if (req == null || string.IsNullOrWhiteSpace(req.Username) || 
                string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.NewPassword))
            {
                _fnSetData.WriteLog("ChangePassword validation failed: Username, Password, and NewPassword are required.");
                return BadRequest(new resp_ChangePassword
                {
                    Success = false,
                    Message = "Username, Password, and NewPassword are required."
                });
            }

            // Validate that new password is different from old password
            if (req.Password == req.NewPassword)
            {
                return BadRequest(new resp_ChangePassword
                {
                    Success = false,
                    Message = "New password must be different from current password."
                });
            }

            string connectionString = await _fnSetData.Decode(_constrings);
            
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Verify old password
                const string verifySql = @"
                    SELECT [UserPassword], [UserLogin]
                    FROM [FMS_API].[dbo].[Registration]
                    WHERE [UserID] = @UserLoginID AND [UserLogin] = @Username;";

                string hashedPassword = null;
                string userLogin = null;

                using (SqlCommand verifyCommand = new SqlCommand(verifySql, connection))
                {
                    verifyCommand.Parameters.AddWithValue("@UserLoginID", userLoginID);
                    verifyCommand.Parameters.AddWithValue("@Username", req.Username);

                    using (var reader = await verifyCommand.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                        {
                            return Unauthorized(new resp_ChangePassword
                            {
                                Success = false,
                                Message = "User not found or username mismatch."
                            });
                        }

                        int passwordOrdinal = reader.GetOrdinal("UserPassword");
                        int loginOrdinal = reader.GetOrdinal("UserLogin");

                        hashedPassword = reader.IsDBNull(passwordOrdinal) ? null : reader.GetString(passwordOrdinal);
                        userLogin = reader.IsDBNull(loginOrdinal) ? null : reader.GetString(loginOrdinal);
                    }
                }

                // Verify old password
                if (string.IsNullOrWhiteSpace(hashedPassword) || !_jwtTokenGenerator.VerifyPassword(req.Password, hashedPassword))
                {
                    _fnSetData.WriteLog($"ChangePassword: Invalid old password for UserLoginID: {userLoginID}");
                    return Unauthorized(new resp_ChangePassword
                    {
                        Success = false,
                        Message = "Current password is incorrect."
                    });
                }

                // Hash new password
                string newHashedPassword;
                try
                {
                    newHashedPassword = _jwtTokenGenerator.HashPassword(req.NewPassword);
                }
                catch (Exception ex)
                {
                    _fnSetData.WriteLog($"ChangePassword hashing failed: {ex.Message}");
                    return StatusCode(StatusCodes.Status500InternalServerError, new resp_ChangePassword
                    {
                        Success = false,
                        Message = "Failed to hash new password."
                    });
                }

                // Call stored procedure to change password
                using (SqlCommand command = new SqlCommand("sp_ChangePassword", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@UserLoginID", userLoginID);
                    command.Parameters.AddWithValue("@NewPassword", newHashedPassword);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            bool success = reader.GetBoolean(reader.GetOrdinal("Success"));
                            string message = reader.IsDBNull(reader.GetOrdinal("Message")) ? 
                                "Unknown error." : reader.GetString(reader.GetOrdinal("Message"));

                            if (success)
                            {
                                _fnSetData.WriteLog($"ChangePassword successful for UserLoginID: {userLoginID}");
                                return Ok(new resp_ChangePassword
                                {
                                    Success = true,
                                    Message = message
                                });
                            }
                            else
                            {
                                _fnSetData.WriteLog($"ChangePassword failed: {message}");
                                return BadRequest(new resp_ChangePassword
                                {
                                    Success = false,
                                    Message = message
                                });
                            }
                        }
                    }
                }

                return StatusCode(StatusCodes.Status500InternalServerError, new resp_ChangePassword
                {
                    Success = false,
                    Message = "No response from stored procedure."
                });
            }
        }
        catch (SqlException sqlEx)
        {
            string errorMessage = $"{DateTime.Now} \n SQL Error in ChangePassword: {sqlEx.Message}";
            foreach (SqlError error in sqlEx.Errors)
            {
                errorMessage += $"\n SQL Error Number: {error.Number} - {error.Message}";
            }
            _fnSetData.WriteLog(errorMessage);
            return StatusCode(StatusCodes.Status500InternalServerError, new resp_ChangePassword
            {
                Success = false,
                Message = "Database error occurred while changing password."
            });
        }
        catch (Exception ex)
        {
            string errorMessage = $"{DateTime.Now} \n Error in ChangePassword: {ex.Message} StackTrace: {ex.StackTrace}";
            _fnSetData.WriteLog(errorMessage);
            return StatusCode(StatusCodes.Status500InternalServerError, new resp_ChangePassword
            {
                Success = false,
                Message = "An error occurred while changing password."
            });
        }
    }

    #region othor
    private string GenerateRandomTokenVersion(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var tokenBuilder = new StringBuilder();

        for (int i = 0; i < length; i++)
        {
            tokenBuilder.Append(chars[random.Next(chars.Length)]);
        }

        return tokenBuilder.ToString();
    }

    public async Task<string> GenerateRandomUserId(int length)
    {
        int s = int.Parse((length / 2).ToString());
        Task<string> task1 = Task.Run(() => render1(s));
        Task<string> task2 = Task.Run(() => render2(s));
        await Task.WhenAll(task1, task2);
        string result1 = await task1;
        string result2 = await task2;
        return result1 + result2;
    }

  


    public string render1(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var userIdBuilder = new StringBuilder();

        for (int i = 0; i < length; i++)
        {
            userIdBuilder.Append(chars[random.Next(chars.Length)]);
        }

        return userIdBuilder.ToString();
    }
    public string render2(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-*#$@!";
        var random = new Random();
        var userIdBuilder = new StringBuilder();

        for (int i = 0; i < length; i++)
        {
            userIdBuilder.Append(chars[random.Next(chars.Length)]);
        }

        return userIdBuilder.ToString();
    }

 

   
    [HttpPost]
    public async Task<IActionResult> SignUpWithStoredProcedure(API_FMS.Models.req_InsertUserLogin req)
    {
        try
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;

            int RoleId = 0;
            if (!string.IsNullOrEmpty(roleClaim))
                RoleId = int.Parse(roleClaim);
            // Validate required fields
            if (string.IsNullOrEmpty(req.LoginName) || string.IsNullOrEmpty(req.LoginPassword) || string.IsNullOrEmpty(req.LoginEmail))
            {
                return new BadRequestObjectResult("LoginName, LoginPassword, and LoginEmail are required");
            }
            string connettion = await _fnSetData.Decode(_constrings);

            // Check if user already exists
            var userExists = await CheckUserExists(req.LoginName, req.LoginEmail, connettion);
            if (userExists)
            {
                return new StatusCodeResult(StatusCodes.Status403Forbidden);
            }

            // Generate unique user ID
            string userId = await GenerateRandomUserId(10);

            // Prepare role and company IDs as comma-separated strings
            string roleIds = req.RoleIDs != null ? string.Join(",", req.RoleIDs) : null;
            string companyIds = req.CompanyIDs != null ? string.Join(",", req.CompanyIDs) : null;

            // Hash password
            string hashedPassword = _jwtTokenGenerator.HashPassword(req.LoginPassword);
            int loginID = 0;
            // Call stored procedure
            using (SqlConnection connection = new SqlConnection(connettion))
            {
                await connection.OpenAsync();

                using (SqlCommand command = new SqlCommand("sp_UpsertUserLoginWithRoles", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    // Add parameters
                    command.Parameters.AddWithValue("@loginID", userId);
                    command.Parameters.AddWithValue("@LoginName", req.LoginName ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@LoginPassword", hashedPassword ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@LoginEmail", req.LoginEmail ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@IsActive", req.IsActive);
                    command.Parameters.AddWithValue("@IsDelete", req.IsDelete);
                    command.Parameters.AddWithValue("@ApproveBy", req.ApproveBy ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@IsNew", req.isNew);
                    command.Parameters.AddWithValue("@CreateBy", "admin");
                    command.Parameters.AddWithValue("@UpdateBy", "admin");
                    command.Parameters.AddWithValue("@RoleIDs", roleIds ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CompanyIDs", companyIds ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Action", req.Action == "update" ? req.Action : null);
                    command.Parameters.AddWithValue("@Role", RoleId);

                    // Execute and get the returned LoginID
                    var result = await command.ExecuteScalarAsync();

                    // After: Safe conversion with validation
                    if (result == null || result == DBNull.Value)
                    {
                        throw new InvalidOperationException("Stored procedure did not return a valid LoginID");
                    }
                    loginID = Convert.ToInt32(result);
                }
            }

            return StatusCode(StatusCodes.Status200OK, new
            {
                Message = "User created successfully",
                LoginID = loginID,
                UserID = req.loginID
            });
        }
        catch (Exception ex)
        {
            string errorMessage = $"{DateTime.Now} \n Error: {ex.Message} StackTrace: {ex.StackTrace}";
            _fnSetData.WriteLog(errorMessage);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }


    [HttpPost]
    public async Task<IActionResult> UpdateUserWithStoredProcedure(API_FMS.Models.req_InsertUserLogin req)
    {
        try
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;

            int RoleId = 0;
            if (!string.IsNullOrEmpty(roleClaim))
                RoleId = int.Parse(roleClaim);

            // Validate required fields
            if (req.loginID == null)
            {
                return new BadRequestObjectResult("LoginUserID is required for update");
            }

            // Prepare role and company IDs as comma-separated strings
            string roleIds = req.RoleIDs != null ? string.Join(",", req.RoleIDs) : null;
            string companyIds = req.CompanyIDs != null ? string.Join(",", req.CompanyIDs) : null;

            // Hash password if provided
            string hashedPassword = !string.IsNullOrEmpty(req.LoginPassword)
                ? _jwtTokenGenerator.HashPassword(req.LoginPassword)
                : null;

            // Call stored procedure
            int loginId = await ExecuteUpsertUserStoredProcedure(
                userId: req.loginID ?? 0,
                loginName: req.LoginName,
                loginPassword: hashedPassword,
                loginEmail: req.LoginEmail,
                isActive: req.IsActive,
                isDelete: req.IsDelete,
                approveBy: req.ApproveBy,
                isNew: req.isNew ?? false,
                createBy: "API",
                updateBy: "API",
                roleIds: roleIds,
                companyIds: companyIds,
                action: "UPDATE",
                Role: RoleId
            );

            return StatusCode(StatusCodes.Status200OK, new
            {
                Message = "User updated successfully",
                LoginID = loginId,
                UserID = req.loginID
            });
        }
        catch (Exception ex)
        {
            string errorMessage = $"{DateTime.Now} \n Error: {ex.Message} StackTrace: {ex.StackTrace}";
            _fnSetData.WriteLog(errorMessage);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
    private async Task<int> ExecuteUpsertUserStoredProcedure(
            int userId,
            string loginName,
            string loginPassword,
            string loginEmail,
            bool isActive,
            bool isDelete,
            string approveBy,
            bool isNew,
            string createBy,
            string updateBy,
            string roleIds,
            string companyIds,
            string action,
            int Role)
    {
        string connettion = await _fnSetData.Decode(_constrings);
        using (SqlConnection connection = new SqlConnection(connettion))
        {
            await connection.OpenAsync();

            using (SqlCommand command = new SqlCommand("sp_UpsertUserLoginWithRoles", connection))
            {
                command.CommandType = CommandType.StoredProcedure;

                // Add parameters
                command.Parameters.AddWithValue("@loginID", userId);
                command.Parameters.AddWithValue("@LoginName", loginName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@LoginPassword", loginPassword ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@LoginEmail", loginEmail ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@IsActive", isActive);
                command.Parameters.AddWithValue("@IsDelete", isDelete);
                command.Parameters.AddWithValue("@ApproveBy", approveBy ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@IsNew", isNew);
                command.Parameters.AddWithValue("@CreateBy", createBy);
                command.Parameters.AddWithValue("@UpdateBy", updateBy);
                command.Parameters.AddWithValue("@RoleIDs", roleIds ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@CompanyIDs", companyIds ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Action", action);
                command.Parameters.AddWithValue("@Role", Role);

                // Execute and get the returned LoginID
                var result = await command.ExecuteScalarAsync();

                // After: Safe conversion with validation
                if (result == null || result == DBNull.Value)
                {
                    throw new InvalidOperationException("Stored procedure did not return a valid LoginID");
                }
                return Convert.ToInt32(result);
            }
        }
    }


    private async Task<bool> CheckUserExists(string loginName, string loginEmail, string con)
    {

        using (SqlConnection connection = new SqlConnection(con))
        {
            await connection.OpenAsync();

            string query = @"
                SELECT COUNT(*) 
                FROM [ADI_Human].[dbo].[AppLogin] 
                WHERE LoginName = @LoginName OR LoginEmail = @LoginEmail";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@LoginName", loginName);
                command.Parameters.AddWithValue("@LoginEmail", loginEmail);

                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                await connection.CloseAsync();
                return count > 0;
            }

        }


    }

    #endregion

    #region File Upload Helpers
    private (bool IsValid, string? ErrorMessage) ValidateImage(IFormFile file)
    {
        if (file.Length > MaxImageSizeBytes)
        {
            return (false, $"File size exceeds {MaxImageSizeBytes / (1024 * 1024)}MB limit.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            return (false, "Invalid file type. Only .png, .jpg or .jpeg are allowed.");
        }

        return (true, null);
    }

    private async Task<(string RelativePath, string PublicUrl)> SaveImageAsync(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var uploadsRootFolder = Path.Combine(GetWebRootPath(), "uploads", "registrations");
        Directory.CreateDirectory(uploadsRootFolder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadsRootFolder, fileName);

        await using (var stream = new FileStream(physicalPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = Path.Combine("uploads", "registrations", fileName).Replace("\\", "/");
        var request = HttpContext.Request;
        var publicUrl = $"{request.Scheme}://{request.Host}/{relativePath}";
        return (relativePath, publicUrl);
    }

    private string GetWebRootPath()
    {
        if (!string.IsNullOrEmpty(_environment.WebRootPath))
        {
            return _environment.WebRootPath;
        }

        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        if (!Directory.Exists(defaultPath))
        {
            Directory.CreateDirectory(defaultPath);
        }

        return defaultPath;
    }

    private void DeleteImageFile(string? relativePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            var webRootPath = GetWebRootPath();
            var physicalPath = Path.Combine(webRootPath, relativePath.Replace("/", "\\"));

            if (System.IO.File.Exists(physicalPath))
            {
                System.IO.File.Delete(physicalPath);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't throw - image deletion failure shouldn't break the flow
            _fnSetData.WriteLog($"Error deleting image file: {ex.Message}");
        }
    }
    #endregion

}

