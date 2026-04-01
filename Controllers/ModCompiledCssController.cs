using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.JellyFrame.Services;
using System.Collections.Generic;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("JellyFrame/mods/{modId}/compiled.css")]
    public class ModCompiledCssController : ControllerBase
    {
        [HttpGet]
        [Produces("text/css")]
        public async Task<IActionResult> GetCompiledCss(string modId)
        {
            var config = Plugin.Instance?.Configuration;
            var paths = Plugin.Instance?.AppPaths;

            if (config == null || paths == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (string.IsNullOrWhiteSpace(config.CachedMods))
                return NotFound();

            List<ModEntry> mods;
            try
            {
                mods = JsonSerializer.Deserialize<List<ModEntry>>(config.CachedMods,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return StatusCode(500); }

            var mod = mods?.Find(m =>
                string.Equals(m.Id, modId, System.StringComparison.OrdinalIgnoreCase));

            if (mod == null || string.IsNullOrWhiteSpace(mod.CssUrl))
                return NotFound();

            var modVars = new Dictionary<string, Dictionary<string, string>>();
            if (!string.IsNullOrWhiteSpace(config.ModVars) && config.ModVars != "{}")
            {
                try
                {
                    modVars = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                        config.ModVars,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? modVars;
                }
                catch { }
            }

            var vars = BuildVarMap(mod, modVars);
            var css = await ModResourceCache.GetCssAsync(mod, vars, paths);

            if (string.IsNullOrWhiteSpace(css))
                return NotFound();

            Response.Headers.Append("Cache-Control", "public, max-age=86400");
            return Content(css, "text/css; charset=utf-8");
        }

        private static Dictionary<string, string> BuildVarMap(
            ModEntry mod,
            Dictionary<string, Dictionary<string, string>> modVars)
        {
            var result = new Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var v in mod.Vars ?? new System.Collections.Generic.List<ModVar>())
                if (!string.IsNullOrEmpty(v.Key))
                    result[v.Key] = v.Default ?? string.Empty;

            if (modVars.TryGetValue(mod.Id ?? string.Empty, out var saved))
                foreach (var kv in saved)
                    result[kv.Key] = kv.Value;

            return result;
        }
    }
}
