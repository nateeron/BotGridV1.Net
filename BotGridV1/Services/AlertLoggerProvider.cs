using Microsoft.Extensions.Logging;
using BotGridV1.Models;

namespace BotGridV1.Services
{
    /// <summary>
    /// Custom logger provider that captures logs and sends to AlertLogService
    /// </summary>
    public class AlertLoggerProvider : ILoggerProvider
    {
        private readonly AlertLogService? _alertLogService;
        private readonly LogLevel _minLevel;

        public AlertLoggerProvider(AlertLogService? alertLogService, LogLevel minLevel = LogLevel.Information)
        {
            _alertLogService = alertLogService;
            _minLevel = minLevel;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new AlertLogger(categoryName, _alertLogService, _minLevel);
        }

        public void Dispose() { }
    }

    public class AlertLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly AlertLogService? _alertLogService;
        private readonly LogLevel _minLevel;

        public AlertLogger(string categoryName, AlertLogService? alertLogService, LogLevel minLevel)
        {
            _categoryName = categoryName;
            _alertLogService = alertLogService;
            _minLevel = minLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minLevel && _alertLogService != null;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var level = logLevel.ToString();

            // Only log important levels to avoid spam
            if (logLevel >= LogLevel.Warning)
            {
                // Fire and forget - explicitly use Func<Task> to avoid compiler confusion
                Func<Task> logAction = async () =>
                {
                    try
                    {
                        await _alertLogService!.AddLogAsync(new AlertLog
                        {
                            Type = "LOG",
                            Level = level,
                            Title = $"[{_categoryName}] {logLevel}",
                            Message = message,
                            Details = exception?.ToString(),
                            Color = GetColorForLogLevel(logLevel),
                            Timestamp = DateTime.UtcNow
                        }).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore errors in logging
                    }
                };
                
                _ = Task.Run(logAction);
            }
        }

        private int GetColorForLogLevel(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Error => 0xe74c3c,      // Red
                LogLevel.Warning => 0xf39c12,    // Orange
                LogLevel.Information => 0x3498db, // Blue
                LogLevel.Debug => 0x95a5a6,      // Gray
                _ => 0x95a5a6                    // Gray
            };
        }
    }
}

