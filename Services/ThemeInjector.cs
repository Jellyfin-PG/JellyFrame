using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyFrame.Configuration;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyFrame.Services
{
    /// <summary>
    /// Injects the active theme into every Jellyfin index.html response.
    ///
    /// Instead of inlining CSS, the injector pre-warms the theme cache
    /// (downloading and compiling all CSS on the server side) then emits a
    /// single &lt;link&gt; tag pointing at
    /// <c>/JellyFrame/themes/{id}/compiled.css?v={hash}</c>.
    ///
    /// The hash is derived from the current var values so the browser
    /// re-fetches automatically whenever the user changes theme settings,
    /// while still caching aggressively between saves.
    /// </summary>
    public static class ThemeInjector
    {
        private const string StartMarker = "<!-- JellyFrame-Theme-Start -->";
        private const string EndMarker = "<!-- JellyFrame-Theme-End -->";

        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private static void Log(bool debug, string msg)
        {
            if (debug) Console.Error.WriteLine("[JellyFrame:Theme] " + msg);
        }

        public static string InjectTheme(string html, PluginConfiguration config, IApplicationPaths paths)
        {
            try
            {
                if (string.IsNullOrEmpty(html) || !html.Contains("</body>"))
                    return html ?? string.Empty;

                bool dbg = config.DebugLogging;

                html = Regex.Replace(
                    html,
                    Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
                    string.Empty);

                if (string.IsNullOrWhiteSpace(config.ActiveTheme) ||
                    string.IsNullOrWhiteSpace(config.CachedThemes))
                {
                    Log(dbg, "No active theme configured.");
                    return html;
                }

                List<ThemeEntry> themes;
                try
                {
                    themes = JsonSerializer.Deserialize<List<ThemeEntry>>(config.CachedThemes, JsonOpts)
                             ?? new List<ThemeEntry>();
                }
                catch (Exception ex)
                {
                    Log(dbg, "Failed to parse CachedThemes: " + ex.Message);
                    return html;
                }

                var theme = themes.Find(t =>
                    string.Equals(t.Id, config.ActiveTheme, StringComparison.OrdinalIgnoreCase));

                if (theme == null)
                {
                    Log(dbg, "Active theme '" + config.ActiveTheme + "' not found in cache.");
                    return html;
                }

                Log(dbg, "Injecting theme: " + theme.Name + " v" + theme.Version);

                var vars = BuildVarMap(theme, config);

                WarmCache(theme, vars, paths, dbg);

                var hash = HashVars(vars);
                var linkHref = "/JellyFrame/themes/" + Uri.EscapeDataString(theme.Id)
                             + "/compiled.css?v=" + hash;

                var sb = new System.Text.StringBuilder();
                sb.Append("\n").Append(StartMarker).Append("\n");

                if (theme.Preconnect != null)
                {
                    foreach (var origin in theme.Preconnect)
                    {
                        if (string.IsNullOrWhiteSpace(origin)) continue;
                        var safe = origin.Trim().Replace("&", "&amp;").Replace("\"", "&quot;");
                        sb.Append("<link rel=\"preconnect\" href=\"").Append(safe).Append("\">\n");
                        sb.Append("<link rel=\"dns-prefetch\" href=\"").Append(safe).Append("\">\n");
                    }
                }

                sb.Append("<link rel=\"stylesheet\" data-jellyframe-theme=\"1\"")
                  .Append(" href=\"").Append(linkHref).Append("\">\n");
                sb.Append(EndMarker).Append("\n");

                html = Regex.Replace(html, @"(</head>)", sb.ToString() + "$1");
                Log(dbg, "Theme link injected: " + linkHref);
                return html;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[JellyFrame:Theme] EXCEPTION: " + ex);
                return html;
            }
        }

        /// <summary>
        /// Pre-warms the disk cache for the theme and all active addons so that
        /// the compiled CSS controller can serve them immediately without waiting
        /// for a download on the first request.
        /// </summary>
        private static void WarmCache(
            ThemeEntry theme,
            Dictionary<string, string> vars,
            IApplicationPaths paths,
            bool dbg)
        {
            if (!string.IsNullOrWhiteSpace(theme.CssUrl))
            {
                var baseCss = ThemeResourceCache.GetThemeCssAsync(
                    theme.Id, theme.Version, theme.CssUrl, vars, paths)
                    .GetAwaiter().GetResult();
                Log(dbg, "Cache warmed: base CSS (" + (baseCss?.Length ?? 0) + " chars)");
            }

            foreach (var addon in theme.Addons ?? new List<ThemeAddon>())
            {
                if (string.IsNullOrWhiteSpace(addon.CssUrl)) continue;

                bool active = string.IsNullOrEmpty(addon.TriggerVar) ||
                              (vars.TryGetValue(addon.TriggerVar, out var tv) &&
                               string.Equals(tv, "true", StringComparison.OrdinalIgnoreCase));

                if (!active)
                {
                    Log(dbg, "Addon '" + addon.Id + "' skipped (trigger var off)");
                    continue;
                }

                var addonCss = ThemeResourceCache.GetAddonCssAsync(
                    theme.Id, addon.Id, theme.Version, addon.CssUrl, vars, paths)
                    .GetAwaiter().GetResult();
                Log(dbg, "Cache warmed: addon '" + addon.Id + "' (" + (addonCss?.Length ?? 0) + " chars)");
            }
        }

        private static Dictionary<string, string> BuildVarMap(
            ThemeEntry theme, PluginConfiguration config)
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

        private static string HashVars(Dictionary<string, string> vars)
        {
            if (vars == null || vars.Count == 0) return "default";
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
    }

    public class ThemeEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string Jellyfin { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public string PreviewUrl { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string CssUrl { get; set; }
        public List<string> Preconnect { get; set; } = new List<string>();
        public List<ThemeVar> Vars { get; set; } = new List<ThemeVar>();
        public List<ThemeAddon> Addons { get; set; } = new List<ThemeAddon>();
    }

    public class ThemeVar
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = "text";
        public string Default { get; set; } = string.Empty;
        public bool? AllowGradient { get; set; } = null;
    }

    public class ThemeAddon
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CssUrl { get; set; }
        public string TriggerVar { get; set; }
    }
}