using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BotGridV1.Models.SQLite;
using BotGridV1.Services;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class ReportController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ReportController> _logger;
        private readonly CurrencyService _currencyService;

        public ReportController(ApplicationDbContext context, ILogger<ReportController> logger, CurrencyService currencyService)
        {
            _context = context;
            _logger = logger;
            _currencyService = currencyService;
        }

        /// <summary>
        /// Get order report statistics
        /// รายงานสถิติการสั่งซื้อ
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetOrderReport()
        {
            try
            {
                await _context.Database.EnsureCreatedAsync();

                // Total Order All - count all orders
                var totalOrderAll = await _context.DbOrders.CountAsync();

                // Order SOLD - count orders with Status = "SOLD"
                var orderSold = await _context.DbOrders
                    .CountAsync(o => o.Status == "SOLD");

                // Order WAITING - count orders with Status = "WAITING_SELL"
                var orderWaiting = await _context.DbOrders
                    .CountAsync(o => o.Status == "WAITING_SELL");

                // Sold Profit Loss - sum of ProfitLoss for SOLD orders
                // SQLite doesn't support Sum on decimal, so we load into memory first
                var soldOrders = await _context.DbOrders
                    .Where(o => o.Status == "SOLD" && o.ProfitLoss.HasValue)
                    .Select(o => o.ProfitLoss!.Value)
                    .ToListAsync();
                
                var soldProfitLossUsdt = soldOrders.Sum();

                // Convert USDT to THB using real-time exchange rate
                var soldProfitLossThb = await _currencyService.ConvertUsdToThbAsync(soldProfitLossUsdt);
                var usdToThbRate = await _currencyService.GetUsdToThbRateAsync();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        totalOrderAll = totalOrderAll,
                        orderSold = orderSold,
                        orderWaiting = orderWaiting,
                        soldProfitLoss = new
                        {
                            usdt = Math.Round(soldProfitLossUsdt, 2),
                            thb = Math.Round(soldProfitLossThb, 2),
                            exchangeRate = Math.Round(usdToThbRate, 4)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order report");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message,
                    error = ex.ToString()
                });
            }
        }
    }
}
