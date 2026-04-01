using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("JellyFrame/mods/{modId}/webhooks/{name}")]
    public class WebhookController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Handle(string modId, string name)
        {
            var loader = Plugin.Instance?.ModLoader;
            if (loader == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var headers = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var h in Request.Headers)
                headers[h.Key] = h.Value.ToString();

            bool handled = loader.DispatchWebhook(modId, name, body, headers);

            if (!handled)
                return NotFound(new { error = "No webhook handler registered", modId, name });

            return Ok(new { ok = true });
        }
    }
}
