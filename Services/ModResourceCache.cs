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
    public static class ModResourceCache
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _urlLocks
            = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private static SemaphoreSlim GetUrlLock(string url)
            => _urlLocks.GetOrAdd(url, _ => new SemaphoreSlim(1, 1));

        public static Task<string> GetJsAsync(
            ModEntry mod,
            Dictionary<string, string> vars,
            IApplicationPaths paths)
            => GetResourceAsync(mod, "js", mod.JsUrl, vars, paths);

        public static Task<string> GetCssAsync(
            ModEntry mod,
            Dictionary<string, string> vars,
            IApplicationPaths paths)
            => GetResourceAsync(mod, "css", mod.CssUrl, vars, paths);

        public static Task<string> GetServerJsAsync(
            ModEntry mod,
            IApplicationPaths paths)
            => GetResourceAsync(mod, "serverjs", mod.ServerJs, vars: null, paths: paths);

        public static void InvalidateMod(string modId, IApplicationPaths paths)
        {
            var cacheDir = GetCacheDir(paths);
            if (!Directory.Exists(cacheDir)) return;

            foreach (var file in Directory.GetFiles(cacheDir, SafeId(modId) + "__*"))
            {
                try { File.Delete(file); } catch { }
            }
        }

        public static void ClearAll(IApplicationPaths paths)
        {
            var cacheDir = GetCacheDir(paths);
            if (!Directory.Exists(cacheDir)) return;

            foreach (var file in Directory.GetFiles(cacheDir))
            {
                try { File.Delete(file); } catch { }
            }
        }

        public static void InvalidateCompiled(string modId, IApplicationPaths paths)
        {
            var cacheDir = GetCacheDir(paths);
            if (!Directory.Exists(cacheDir)) return;

            foreach (var file in Directory.GetFiles(cacheDir, SafeId(modId) + "__*"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Split(new[] { "__" }, StringSplitOptions.None).Length == 4)
                    try { File.Delete(file); } catch { }
            }
        }

        private static async Task<string> GetResourceAsync(
            ModEntry mod,
            string type,
            string url,
            Dictionary<string, string> vars,
            IApplicationPaths paths)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            var cacheDir  = GetCacheDir(paths);

            var cacheFile = (type == "serverjs" || vars == null)
                ? GetCacheFilePath(cacheDir, mod.Id, mod.Version, type, varsHash: null)
                : GetCacheFilePath(cacheDir, mod.Id, mod.Version, type, varsHash: HashVars(vars));

            if (File.Exists(cacheFile))
            {
                try { return await File.ReadAllTextAsync(cacheFile, Encoding.UTF8); }
                catch {  }
            }

            var urlLock = GetUrlLock(url);
            await urlLock.WaitAsync();
            try
            {

                if (File.Exists(cacheFile))
                    return await File.ReadAllTextAsync(cacheFile, Encoding.UTF8);

                EvictStale(cacheDir, mod.Id, type);

                string raw;
                try
                {
                    raw = await Http.GetStringAsync(url);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[JellyFrame] Failed to download {type} for '{mod.Id}' from {url}: {ex.Message}");
                    return null;
                }

                string compiled = (type == "serverjs" || vars == null)
                    ? raw
                    : SubstituteVars(raw, vars);

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
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                source = source.Replace(
                    "{{" + kv.Key + "}}",
                    kv.Value ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }

            return source;
        }

        private static string GetCacheDir(IApplicationPaths paths)
            => Path.Combine(paths.DataPath, "JellyFrame", "cache");

        private static string GetCacheFilePath(string cacheDir, string modId, string version, string type, string varsHash)
        {
            var ext  = type == "css" ? "css" : "js";
            var name = varsHash != null
                ? $"{SafeId(modId)}__{SafeId(version)}__{type}__{varsHash}.{ext}"
                : $"{SafeId(modId)}__{SafeId(version)}__{type}.{ext}";
            return Path.Combine(cacheDir, name);
        }

        private static string HashVars(Dictionary<string, string> vars)
        {
            if (vars == null || vars.Count == 0) return "novars";

            var keys = new List<string>(vars.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            foreach (var k in keys)
                sb.Append(k).Append('=').Append(vars[k] ?? string.Empty).Append(';');

            uint h = 2166136261u;
            foreach (char c in sb.ToString())
                h = (h ^ c) * 16777619u;

            return h.ToString("x8");
        }

        private static void EvictStale(string cacheDir, string modId, string type)
        {
            if (!Directory.Exists(cacheDir)) return;

            foreach (var file in Directory.GetFiles(cacheDir, SafeId(modId) + "__*"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var parts = name.Split(new[] { "__" }, StringSplitOptions.None);

                if (parts.Length >= 3 && parts[2] == type)
                    try { File.Delete(file); } catch { }
            }
        }

        private static string SafeId(string s)
            => Regex.Replace(s ?? "unknown", @"[^\w\-.]", "_");
    }
}
