using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyFrame.Services
{
    /// <summary>
    /// Server-side proxy and pre-compiler for theme CSS resources.
    /// Fully segmented from <see cref="ModResourceCache"/> — themes use their
    /// own cache directory and their own per-URL locks.
    ///
    /// Cache directory: {jellyfinDataDir}/JellyFrame/themes/
    /// File naming: {themeId}__{version}__{type}[__{varsHash}].css
    /// </summary>
    public static class ThemeResourceCache
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _urlLocks
            = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private static SemaphoreSlim GetUrlLock(string url)
            => _urlLocks.GetOrAdd(url, _ => new SemaphoreSlim(1, 1));

        public static Task<string> GetThemeCssAsync(
            string themeId,
            string version,
            string cssUrl,
            Dictionary<string, string> vars,
            IApplicationPaths paths)
            => GetResourceAsync(themeId, version, "base", cssUrl, vars, paths);

        public static Task<string> GetAddonCssAsync(
            string themeId,
            string addonId,
            string version,
            string cssUrl,
            Dictionary<string, string> vars,
            IApplicationPaths paths)
            => GetResourceAsync(themeId + "--" + addonId, version, "addon", cssUrl, vars, paths);

        public static void InvalidateTheme(string themeId, IApplicationPaths paths)
        {
            var cacheDir = GetCacheDir(paths);
            if (!Directory.Exists(cacheDir)) return;
            foreach (var file in Directory.GetFiles(cacheDir, SafeId(themeId) + "__*"))
                try { File.Delete(file); } catch { }
        }

        public static void InvalidateAddon(string themeId, string addonId, IApplicationPaths paths)
        {
            var cacheDir = GetCacheDir(paths);
            if (!Directory.Exists(cacheDir)) return;
            var prefix = SafeId(themeId + "--" + addonId) + "__";
            foreach (var file in Directory.GetFiles(cacheDir, prefix + "*"))
                try { File.Delete(file); } catch { }
        }

        public static void InvalidateCompiled(string themeId, IApplicationPaths paths)
        {
            var cacheDir = GetCacheDir(paths);
            if (!Directory.Exists(cacheDir)) return;
            foreach (var file in Directory.GetFiles(cacheDir, SafeId(themeId) + "__*"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Split(new[] { "__" }, StringSplitOptions.None).Length == 4)
                    try { File.Delete(file); } catch { }
            }
        }

        public static void ClearAll(IApplicationPaths paths)
        {
            var cacheDir = GetCacheDir(paths);
            if (!Directory.Exists(cacheDir)) return;
            foreach (var file in Directory.GetFiles(cacheDir))
                try { File.Delete(file); } catch { }
        }

        public static (int fileCount, long totalBytes, string cacheDir) GetInfo(IApplicationPaths paths)
        {
            var dir = GetCacheDir(paths);
            if (!Directory.Exists(dir)) return (0, 0, dir);
            var files = Directory.GetFiles(dir);
            long total = 0;
            foreach (var f in files) try { total += new FileInfo(f).Length; } catch { }
            return (files.Length, total, dir);
        }

        private static async Task<string> GetResourceAsync(
            string id,
            string version,
            string type,
            string url,
            Dictionary<string, string> vars,
            IApplicationPaths paths)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            var cacheDir  = GetCacheDir(paths);
            var cacheFile = vars != null
                ? GetCacheFilePath(cacheDir, id, version, type, HashVars(vars))
                : GetCacheFilePath(cacheDir, id, version, type, null);

            if (File.Exists(cacheFile))
                try { return await File.ReadAllTextAsync(cacheFile, Encoding.UTF8); } catch { }

            var urlLock = GetUrlLock(url);
            await urlLock.WaitAsync();
            try
            {
                if (File.Exists(cacheFile))
                    return await File.ReadAllTextAsync(cacheFile, Encoding.UTF8);

                EvictStale(cacheDir, id, type);

                string raw;
                try { raw = await Http.GetStringAsync(url); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[JellyFrame:Theme] Failed to download {type} for '{id}': {ex.Message}");
                    return null;
                }

                var compiled = vars != null ? SubstituteVars(raw, vars) : raw;
                Directory.CreateDirectory(cacheDir);
                await File.WriteAllTextAsync(cacheFile, compiled, Encoding.UTF8);
                return compiled;
            }
            finally
            {
                urlLock.Release();
            }
        }

        private static string SubstituteVars(string source, Dictionary<string, string> vars)
        {
            if (vars == null || vars.Count == 0) return source;
            foreach (var kv in vars)
                if (!string.IsNullOrEmpty(kv.Key))
                    source = source.Replace("{{" + kv.Key + "}}", kv.Value ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
            return source;
        }

        private static string GetCacheDir(IApplicationPaths paths)
            => Path.Combine(paths.DataPath, "JellyFrame", "themes");

        private static string GetCacheFilePath(string cacheDir, string id, string version, string type, string varsHash)
        {
            var name = varsHash != null
                ? $"{SafeId(id)}__{SafeId(version)}__{type}__{varsHash}.css"
                : $"{SafeId(id)}__{SafeId(version)}__{type}.css";
            return Path.Combine(cacheDir, name);
        }

        private static void EvictStale(string cacheDir, string id, string type)
        {
            if (!Directory.Exists(cacheDir)) return;
            foreach (var file in Directory.GetFiles(cacheDir, SafeId(id) + "__*"))
            {
                var parts = Path.GetFileNameWithoutExtension(file).Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length >= 3 && parts[2] == type)
                    try { File.Delete(file); } catch { }
            }
        }

        private static string HashVars(Dictionary<string, string> vars)
        {
            if (vars == null || vars.Count == 0) return "novars";
            var keys = new System.Collections.Generic.List<string>(vars.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            foreach (var k in keys)
                sb.Append(k).Append('=').Append(vars[k] ?? string.Empty).Append(';');
            uint h = 2166136261u;
            foreach (char c in sb.ToString())
                h = (h ^ c) * 16777619u;
            return h.ToString("x8");
        }

        private static string SafeId(string s)
            => Regex.Replace(s ?? "unknown", @"[^\w\-.]", "_");
    }
}
