using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Api;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    /// <summary>
    /// Provides read/write access to cached mod and theme files for the in-browser
    /// cache editor.
    ///
    /// GET  /JellyFrame/api/cache/files/{id}?kind=mod|theme
    ///      Lists cached files for a given mod or theme, returning filename,
    ///      file type label, and the safe absolute path used by the read/write endpoints.
    ///
    /// GET  /JellyFrame/api/cache/file?path={base64Path}
    ///      Reads and returns the content of a cached file by its absolute path.
    ///      Path must resolve inside the JellyFrame cache directory.
    ///
    /// PUT  /JellyFrame/api/cache/file?path={base64Path}
    ///      Writes new content to a cached file. The file must already exist.
    ///      Path must resolve inside the JellyFrame cache directory.
    /// </summary>
    [ApiController]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Route("JellyFrame/api/cache")]
    public class CacheEditorController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        [HttpGet("files/{id}")]
        public IActionResult ListFiles(string id, [FromQuery] string kind = "mod")
        {
            var paths = Plugin.Instance?.AppPaths;
            if (paths == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            bool isMod = !string.Equals(kind, "theme", StringComparison.OrdinalIgnoreCase);
            var cacheDir = isMod
                ? Path.Combine(paths.DataPath, "JellyFrame", "mods")
                : Path.Combine(paths.DataPath, "JellyFrame", "themes");

            if (!Directory.Exists(cacheDir))
                return Ok(new { files = Array.Empty<object>() });

            var safeId = SafeId(id);
            var results = new List<object>();

            var matchedFiles = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(cacheDir, safeId + "__*"))
                matchedFiles.Add(file);
            foreach (var file in Directory.GetFiles(cacheDir, safeId + "--*"))
                matchedFiles.Add(file);

            foreach (var file in System.Linq.Enumerable.OrderBy(matchedFiles, f => f))
            {
                var info = new FileInfo(file);
                var stem = Path.GetFileNameWithoutExtension(file);

                string typeLabel;
                string tabLabel;
                var doubleDash = stem.IndexOf("--", StringComparison.Ordinal);
                if (doubleDash >= 0)
                {
                    var afterDash = stem.Substring(doubleDash + 2);
                    var addonParts = afterDash.Split(new[] { "__" }, StringSplitOptions.None);
                    var addonId = addonParts.Length >= 1 ? addonParts[0] : "addon";
                    typeLabel = addonParts.Length >= 3 ? addonParts[2] : "addon";
                    tabLabel = "addon: " + addonId;
                }
                else
                {
                    var parts = stem.Split(new[] { "__" }, StringSplitOptions.None);
                    typeLabel = parts.Length >= 3 ? parts[2] : "base";
                    tabLabel = typeLabel;
                }

                var lang = info.Extension.TrimStart('.').ToLowerInvariant();
                var encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(file));

                results.Add(new
                {
                    filename = info.Name,
                    type = typeLabel,
                    tabLabel = tabLabel,
                    lang = lang,
                    sizeBytes = info.Length,
                    modified = info.LastWriteTimeUtc.ToString("o"),
                    path = encodedPath
                });
            }

            return Ok(new { files = results });
        }

        [HttpGet("file")]
        public IActionResult ReadFile([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(new { error = "path is required" });

            string absolutePath;
            try
            {
                absolutePath = Encoding.UTF8.GetString(Convert.FromBase64String(path));
            }
            catch
            {
                return BadRequest(new { error = "invalid path encoding" });
            }

            if (!IsInsideCacheDir(absolutePath))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "path is outside the JellyFrame cache directory" });

            if (!System.IO.File.Exists(absolutePath))
                return NotFound(new { error = "file not found" });

            try
            {
                var content = System.IO.File.ReadAllText(absolutePath, Encoding.UTF8);
                return Ok(new { content, path });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("file")]
        public IActionResult WriteFile([FromQuery] string path, [FromBody] WriteFileRequest body)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(new { error = "path is required" });

            if (body == null)
                return BadRequest(new { error = "request body is required" });

            string absolutePath;
            try
            {
                absolutePath = Encoding.UTF8.GetString(Convert.FromBase64String(path));
            }
            catch
            {
                return BadRequest(new { error = "invalid path encoding" });
            }

            if (!IsInsideCacheDir(absolutePath))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "path is outside the JellyFrame cache directory" });

            if (!System.IO.File.Exists(absolutePath))
                return NotFound(new { error = "file not found — cannot create new cache files via the editor" });

            try
            {
                System.IO.File.WriteAllText(absolutePath, body.Content ?? string.Empty, Encoding.UTF8);
                return Ok(new { ok = true, path });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Resolves the path and verifies it sits inside one of the two JellyFrame
        /// cache directories. Prevents path-traversal attacks.
        /// </summary>
        private bool IsInsideCacheDir(string absolutePath)
        {
            var paths = Plugin.Instance?.AppPaths;
            if (paths == null) return false;

            var modsDir = Path.GetFullPath(Path.Combine(paths.DataPath, "JellyFrame", "mods"));
            var themesDir = Path.GetFullPath(Path.Combine(paths.DataPath, "JellyFrame", "themes"));
            var resolved = Path.GetFullPath(absolutePath);

            return resolved.StartsWith(modsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || resolved.StartsWith(themesDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolved, modsDir, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolved, themesDir, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeId(string s)
            => System.Text.RegularExpressions.Regex.Replace(s ?? "unknown", @"[^\w\-.]", "_");
    }

    public class WriteFileRequest
    {
        public string Content { get; set; }
    }
}
