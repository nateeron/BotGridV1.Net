using System.Text.Json;
using BotGridV1.Models.SQLite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BotGridV1.Services
{
    public static class DefaultDataSeeder
    {
        private const string DefaultSettingsFileName = "default-settings.json";

        public static async Task EnsureSeedDataAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("DefaultDataSeeder");
            var env = scope.ServiceProvider.GetService<IWebHostEnvironment>();

            await EnsureDefaultSettingAsync(
                context,
                logger,
                env?.ContentRootPath ?? AppContext.BaseDirectory);
        }

        public static async Task<int> EnsureDefaultSettingAsync(
            ApplicationDbContext context,
            ILogger logger,
            string? basePath = null)
        {
            await context.Database.EnsureCreatedAsync();

            if (await context.DbSettings.AnyAsync())
            {
                return 0; // Already seeded
            }

            basePath ??= AppContext.BaseDirectory;
            var filePath = Path.Combine(basePath, DefaultSettingsFileName);

            if (!File.Exists(filePath))
            {
                logger.LogWarning("Default settings file not found at {FilePath}", filePath);
                return 0;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var defaultSetting = JsonSerializer.Deserialize<DbSetting>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (defaultSetting == null)
                {
                    logger.LogWarning("Default settings file is empty or invalid at {FilePath}", filePath);
                    return 0;
                }

                // Reset identity fields to avoid conflicts
                defaultSetting.Id = 0;

                context.DbSettings.Add(defaultSetting);
                await context.SaveChangesAsync();

                logger.LogInformation("Inserted default setting from {FilePath}", filePath);
                return 1;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed default settings from {FilePath}", filePath);
                return 0;
            }
        }
    }
}

