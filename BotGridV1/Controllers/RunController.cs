using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotGridV1.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    public class RunController : ControllerBase
    {

        [HttpGet("/")]
        public async Task<IActionResult> Run()
        {
            return Ok("Ok Run .Net C# v.8.0");
        }
    }
}
