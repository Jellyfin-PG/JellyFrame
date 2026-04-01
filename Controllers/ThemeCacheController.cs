using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Api;
using Jellyfin.Plugin.JellyFrame.Services;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    /// <summary>
    /// Cache management for the theme system — fully segmented from the mod cache.
    ///
    /// GET  /JellyFrame/api/themes/cache         — list cached theme files
    /// POST /JellyFrame/api/themes/cache/purge   — purge all or specific theme(s)
    /// </summary>
    [ApiController]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Route("JellyFrame/api/themes/cache")]
    public class ThemeCacheController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetCacheInfo()
        {
            var paths = Plugin.Instance?.AppPaths;
            if (paths == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

            var (fileCount, totalBytes, cacheDir) = ThemeResourceCache.GetInfo(paths);
            var entries = new List<object>();

            if (Directory.Exists(cacheDir))
            {
                foreach (var file in Directory.GetFiles(cacheDir))
                {
                    var info = new FileInfo(file);
                    var name = Path.GetFileNameWithoutExtension(file);
                    var parts = name.Split(new[] { "__" }, StringSplitOptions.None);
                    entries.Add(new
                    {
                        file = info.Name,
                        themeId = parts.Length >= 1 ? parts[0] : "unknown",
                        version = parts.Length >= 2 ? parts[1] : "unknown",
                        type = parts.Length >= 3 ? parts[2] : "unknown",
                        sizeBytes = info.Length,
                        modified = info.LastWriteTimeUtc
                    });
                }
            }

            return Ok(new { cacheDir, fileCount, totalBytes, entries });
        }

        [HttpPost("purge")]
        public IActionResult PurgeCache([FromBody] PurgeThemeCacheRequest body)
        {
            var paths = Plugin.Instance?.AppPaths;
            if (paths == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

            var purged = new List<string>();

            if (body?.ThemeIds != null && body.ThemeIds.Count > 0)
            {
                foreach (var id in body.ThemeIds)
                {
                    ThemeResourceCache.InvalidateTheme(id, paths);
                    purged.Add(id);
                }
            }
            else
            {
                ThemeResourceCache.ClearAll(paths);
                purged.Add("*");
            }

            return Ok(new { ok = true, purged });
        }
    }

    public class PurgeThemeCacheRequest
    {
        public List<string> ThemeIds { get; set; }
    }
}