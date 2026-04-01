using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.JellyFrame.Services;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    /// <summary>
    /// Serves the compiled CSS for the active theme.
    /// Called by the &lt;link&gt; tag injected by <see cref="ThemeInjector"/>.
    ///
    /// Returns base CSS + all active addon CSS concatenated, with
    /// variable substitution applied. The cache is pre-warmed by the
    /// injector, so this endpoint reads from disk only.
    ///
    /// GET /JellyFrame/themes/{themeId}/compiled.css
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("JellyFrame/themes/{themeId}/compiled.css")]
    public class ThemeCompiledCssController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        [HttpGet]
        [Produces("text/css")]
        public async Task<IActionResult> GetCompiledCss(string themeId)
        {
            var config = Plugin.Instance?.Configuration;
            var paths = Plugin.Instance?.AppPaths;

            if (config == null || paths == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (string.IsNullOrWhiteSpace(config.CachedThemes))
                return NotFound();

            List<ThemeEntry> themes;
            try { themes = JsonSerializer.Deserialize<List<ThemeEntry>>(config.CachedThemes, JsonOpts); }
            catch { return StatusCode(500); }

            var theme = themes?.Find(t =>
                string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase));

            if (theme == null || string.IsNullOrWhiteSpace(theme.CssUrl))
                return NotFound();

            var vars = BuildVarMap(theme, config);
            var css = new StringBuilder();

            var baseCss = await ThemeResourceCache.GetThemeCssAsync(
                theme.Id, theme.Version, theme.CssUrl, vars, paths);

            if (!string.IsNullOrWhiteSpace(baseCss))
                css.AppendLine(baseCss);

            foreach (var addon in theme.Addons ?? new List<ThemeAddon>())
            {
                if (string.IsNullOrWhiteSpace(addon.CssUrl)) continue;

                bool active = string.IsNullOrEmpty(addon.TriggerVar) ||
                              (vars.TryGetValue(addon.TriggerVar, out var tv) &&
                               string.Equals(tv, "true", StringComparison.OrdinalIgnoreCase));

                if (!active) continue;

                var addonCss = await ThemeResourceCache.GetAddonCssAsync(
                    theme.Id, addon.Id, theme.Version, addon.CssUrl, vars, paths);

                if (!string.IsNullOrWhiteSpace(addonCss))
                    css.AppendLine(addonCss);
            }

            var result = css.ToString();
            if (string.IsNullOrWhiteSpace(result))
                return NotFound();

            Response.Headers.Append("Cache-Control", "public, max-age=86400");
            return Content(result, "text/css; charset=utf-8");
        }

        private static Dictionary<string, string> BuildVarMap(
            ThemeEntry theme, Configuration.PluginConfiguration config)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in theme.Vars ?? new List<ThemeVar>())
                if (!string.IsNullOrEmpty(v.Key))
                    result[v.Key] = v.Default ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(config.ThemeVars) && config.ThemeVars != "{}")
            {
                try
                {
                    var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        config.ThemeVars, JsonOpts);
                    if (saved != null)
                        foreach (var kv in saved)
                            result[kv.Key] = kv.Value;
                }
                catch { }
            }

            return result;
        }
    }
}