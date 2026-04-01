using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("JellyFrame/mods/{modId}/api/{**path}")]
    public class JellyFrameModsController : ControllerBase
    {
        [HttpGet]
        [HttpPost]
        [HttpPut]
        [HttpDelete]
        [HttpPatch]
        public async Task<IActionResult> Handle(string modId, string path)
        {
            var loader = Plugin.Instance?.ModLoader;
            if (loader == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            var handled = await loader.TryHandleRequestAsync(HttpContext);
            if (!handled)
                return NotFound(new { error = "No route matched", modId, path });

            return new EmptyResult();
        }
    }
}
