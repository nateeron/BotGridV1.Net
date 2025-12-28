using System.Net.Http;
using System.Text.Json;

namespace BotGridV1.Services
{
    public class CurrencyService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CurrencyService> _logger;
        private decimal? _cachedUsdToThbRate = null;
        private DateTime? _cacheTime = null;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5); // Cache for 5 minutes

        public CurrencyService(HttpClient httpClient, ILogger<CurrencyService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Get USD to THB exchange rate (cached for 5 minutes)
        /// </summary>
        public async Task<decimal> GetUsdToThbRateAsync()
        {
            // Return cached rate if still valid
            if (_cachedUsdToThbRate.HasValue && _cacheTime.HasValue)
            {
                var cacheAge = DateTime.UtcNow - _cacheTime.Value;
                if (cacheAge < _cacheDuration)
                {
                    return _cachedUsdToThbRate.Value;
                }
            }

            try
            {
                // Using exchangerate-api.io (free tier, no API key required)
                // Alternative: You can use other APIs like fixer.io, currencyapi.net, etc.
                var response = await _httpClient.GetAsync("https://api.exchangerate-api.com/v4/latest/USD");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(content);
                    
                    if (jsonDoc.RootElement.TryGetProperty("rates", out var rates))
                    {
                        if (rates.TryGetProperty("THB", out var thbRate))
                        {
                            var rate = thbRate.GetDecimal();
                            _cachedUsdToThbRate = rate;
                            _cacheTime = DateTime.UtcNow;
                            _logger.LogInformation($"USD to THB rate updated: {rate}");
                            return rate;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch USD to THB rate from API, using cached value or default");
            }

            // Return cached value if API call failed
            if (_cachedUsdToThbRate.HasValue)
            {
                return _cachedUsdToThbRate.Value;
            }

            // Fallback to approximate rate if no cache available
            _logger.LogWarning("Using fallback USD to THB rate: 35.00");
            return 35.00m; // Approximate fallback rate
        }

        /// <summary>
        /// Convert USD amount to THB
        /// </summary>
        public async Task<decimal> ConvertUsdToThbAsync(decimal usdAmount)
        {
            var rate = await GetUsdToThbRateAsync();
            return usdAmount * rate;
        }
    }
}
