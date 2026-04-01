using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Api;
using Jellyfin.Plugin.JellyFrame.Services;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    /// <summary>
    /// Cache management for the mod system.
    ///
    /// GET  /JellyFrame/api/mods/cache          — list cached mod files
    /// POST /JellyFrame/api/mods/cache/purge    — purge all or specific mod(s)
    /// </summary>
    [ApiController]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Route("JellyFrame/api/mods/cache")]
    public class ModCacheController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetCacheInfo()
        {
            var paths = Plugin.Instance?.AppPaths;
            if (paths == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

            var cacheDir = Path.Combine(paths.DataPath, "JellyFrame", "mods");
            var entries = new List<object>();
            long totalBytes = 0;

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
                        modId = parts.Length >= 1 ? parts[0] : "unknown",
                        version = parts.Length >= 2 ? parts[1] : "unknown",
                        type = parts.Length >= 3 ? parts[2] : "unknown",
                        sizeBytes = info.Length,
                        modified = info.LastWriteTimeUtc
                    });
                    totalBytes += info.Length;
                }
            }

            return Ok(new { cacheDir, fileCount = entries.Count, totalBytes, entries });
        }

        [HttpPost("purge")]
        public IActionResult PurgeCache([FromBody] PurgeModCacheRequest body)
        {
            var paths = Plugin.Instance?.AppPaths;
            if (paths == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

            int deleted = 0;
            var purged = new List<string>();

            if (body?.ModIds != null && body.ModIds.Count > 0)
            {
                foreach (var modId in body.ModIds)
                {
                    var before = CountFiles(paths);
                    ModResourceCache.InvalidateMod(modId, paths);
                    deleted += before - CountFiles(paths);
                    purged.Add(modId);
                }
            }
            else
            {
                var before = CountFiles(paths);
                ModResourceCache.ClearAll(paths);
                deleted = before;
                purged.Add("*");
            }

            return Ok(new { ok = true, deleted, purged });
        }

        private static int CountFiles(MediaBrowser.Common.Configuration.IApplicationPaths paths)
        {
            var dir = Path.Combine(paths.DataPath, "JellyFrame", "mods");
            return Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0;
        }
    }

    public class PurgeModCacheRequest
    {
        public List<string> ModIds { get; set; }
    }
}