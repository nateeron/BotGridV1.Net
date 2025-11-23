using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BotGridV1.Services;
using BotGridV1.Models.Binace;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class DiscoedController : ControllerBase
    {
        private readonly DiscordService _discordService;
        private readonly ILogger<DiscoedController> _logger;

        public DiscoedController(DiscordService discordService, ILogger<DiscoedController> logger)
        {
            _discordService = discordService;
            _logger = logger;
        }

        /// <summary>
        /// Log Error to Discord
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LogError(req_DiscordLog req)
        {
            try
            {
                await _discordService.LogErrorAsync(
                    req.Webhook1,
                    req.Webhook2,
                    req.Message ?? "Unknown error",
                    req.Details
                );

                return Ok(new
                {
                    success = true,
                    message = "Error logged to Discord"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging to Discord");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Log Buy action to Discord
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LogBuy(req_DiscordBuyLog req)
        {
            try
            {
                await _discordService.LogBuyAsync(
                    req.Webhook1,
                    req.Webhook2,
                    req.Symbol ?? "UNKNOWN",
                    req.Price,
                    req.Quantity,
                    req.BuyAmount,
                    req.OrderId ?? "N/A"
                );

                return Ok(new
                {
                    success = true,
                    message = "Buy action logged to Discord"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging buy to Discord");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Log Sell action to Discord
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LogSell(req_DiscordSellLog req)
        {
            try
            {
                await _discordService.LogSellAsync(
                    req.Webhook1,
                    req.Webhook2,
                    req.Symbol ?? "UNKNOWN",
                    req.Price,
                    req.Quantity,
                    req.ProfitLoss,
                    req.OrderId ?? "N/A"
                );

                return Ok(new
                {
                    success = true,
                    message = "Sell action logged to Discord"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging sell to Discord");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Log Start action to Discord
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LogStart(req_DiscordStartLog req)
        {
            try
            {
                await _discordService.LogStartAsync(
                    req.Webhook1,
                    req.Webhook2,
                    req.Symbol ?? "UNKNOWN",
                    req.ConfigId
                );

                return Ok(new
                {
                    success = true,
                    message = "Start action logged to Discord"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging start to Discord");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Log Stop action to Discord
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LogStop(req_DiscordStopLog req)
        {
            try
            {
                await _discordService.LogStopAsync(
                    req.Webhook1,
                    req.Webhook2,
                    req.Symbol
                );

                return Ok(new
                {
                    success = true,
                    message = "Stop action logged to Discord"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging stop to Discord");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Log Buy Retry action to Discord
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LogBuyRetry(req_DiscordBuyRetryLog req)
        {
            try
            {
                await _discordService.LogBuyRetryAsync(
                    req.Webhook1,
                    req.Webhook2,
                    req.Symbol ?? "UNKNOWN",
                    req.Price,
                    req.RetryCount,
                    req.Reason ?? "Unknown reason"
                );

                return Ok(new
                {
                    success = true,
                    message = "Buy retry logged to Discord"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging buy retry to Discord");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        /// <summary>
        /// Log Buy Not Success to Discord
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> LogBuyNotSuccess(req_DiscordBuyNotSuccessLog req)
        {
            try
            {
                await _discordService.LogBuyNotSuccessAsync(
                    req.Webhook1,
                    req.Webhook2,
                    req.Symbol ?? "UNKNOWN",
                    req.Error ?? "Unknown error",
                    req.RetryCount
                );

                return Ok(new
                {
                    success = true,
                    message = "Buy not success logged to Discord"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging buy not success to Discord");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }
}
