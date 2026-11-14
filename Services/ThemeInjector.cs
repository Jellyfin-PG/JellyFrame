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
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static readonly Regex _themeBlockRegex = new Regex(
            Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
            RegexOptions.Compiled);

        private static readonly Regex _preconnectBlockRegex = new Regex(
            @"<!-- JellyFrame-Theme-Preconnect-Start -->[\s\S]*?<!-- JellyFrame-Theme-Preconnect-End -->\n?",
            RegexOptions.Compiled);

        private static readonly Regex _headTagRegex = new Regex(@"(</head>)", RegexOptions.Compiled);
        private static readonly Regex _bodyTagRegex = new Regex(@"(</body>)", RegexOptions.Compiled);

        // Pre-computed injection cache
        private static string _cachedPreconnectMarkup;
        private static string _cachedThemeLinkMarkup;
        private static volatile bool _cacheValid;
        private static readonly object _buildLock = new object();

        public static void InvalidateCache()
        {
            _cacheValid = false;
        }

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

                bool dbg = config?.DebugLogging ?? false;

                // Strip any existing theme markup using compiled regexes
                html = _themeBlockRegex.Replace(html, string.Empty);
                html = _preconnectBlockRegex.Replace(html, string.Empty);

                if (config == null ||
                    string.IsNullOrWhiteSpace(config.ActiveTheme) ||
                    string.IsNullOrWhiteSpace(config.CachedThemes))
                {
                    Log(dbg, "No active theme configured.");
                    return html;
                }

                if (!_cacheValid)
                {
                    lock (_buildLock)
                    {
                        if (!_cacheValid)
                        {
                            RebuildCache(config, paths, dbg);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(_cachedPreconnectMarkup))
                {
                    html = _headTagRegex.Replace(html, _cachedPreconnectMarkup + "$1");
                }

                if (!string.IsNullOrEmpty(_cachedThemeLinkMarkup))
                {
                    html = _bodyTagRegex.Replace(html, _cachedThemeLinkMarkup + "$1");
                    Log(dbg, "Theme link injected");
                }

                return html;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[JellyFrame:Theme] EXCEPTION: " + ex);
                return html;
            }
        }

        private static void RebuildCache(PluginConfiguration config, IApplicationPaths paths, bool dbg)
        {
            _cachedPreconnectMarkup = null;
            _cachedThemeLinkMarkup = null;

            List<ThemeEntry> themes;
            try
            {
                themes = JsonSerializer.Deserialize<List<ThemeEntry>>(config.CachedThemes, JsonOpts)
                         ?? new List<ThemeEntry>();
            }
            catch (Exception ex)
            {
                Log(dbg, "Failed to parse CachedThemes: " + ex.Message);
                _cacheValid = true;
                return;
            }

            var theme = themes.Find(t =>
                string.Equals(t.Id, config.ActiveTheme, StringComparison.OrdinalIgnoreCase));

            if (theme == null)
            {
                Log(dbg, "Active theme '" + config.ActiveTheme + "' not found in cache.");
                _cacheValid = true;
                return;
            }

            if (!VersionRangeMatcher.IsCompatible(theme.Jellyfin, Plugin.ServerVersion))
            {
                Log(true, $"Theme '{theme.Id}' requires Jellyfin '{theme.Jellyfin}', but server is '{Plugin.ServerVersion}' — skipping theme injection");
                _cacheValid = true;
                return;
            }

            Log(dbg, "Rebuilding theme injection cache: " + theme.Name + " v" + theme.Version);

            var vars = BuildVarMap(theme, config);
            WarmCache(theme, vars, paths, dbg);

            var hash = HashVars(vars);
            var linkHref = "/JellyFrame/themes/" + Uri.EscapeDataString(theme.Id)
                         + "/compiled.css?v=" + hash;

            if (theme.Preconnect != null && theme.Preconnect.Count > 0)
            {
                var headSb = new System.Text.StringBuilder();
                headSb.Append("\n<!-- JellyFrame-Theme-Preconnect-Start -->\n");
                foreach (var origin in theme.Preconnect)
                {
                    if (string.IsNullOrWhiteSpace(origin)) continue;
                    var safe = origin.Trim().Replace("&", "&amp;").Replace("\"", "&quot;");
                    headSb.Append("<link rel=\"preconnect\" href=\"").Append(safe).Append("\">\n");
                    headSb.Append("<link rel=\"dns-prefetch\" href=\"").Append(safe).Append("\">\n");
                }
                headSb.Append("<!-- JellyFrame-Theme-Preconnect-End -->\n");
                _cachedPreconnectMarkup = headSb.ToString();
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("\n").Append(StartMarker).Append("\n");
            sb.Append("<link rel=\"stylesheet\" data-jellyframe-theme=\"1\"")
              .Append(" href=\"").Append(linkHref).Append("\">\n");
            sb.Append(EndMarker).Append("\n");
            _cachedThemeLinkMarkup = sb.ToString();

            _cacheValid = true;
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
            var baseCssFiles = theme.GetMatchingCssFiles(Plugin.ServerVersion);
            foreach (var baseFile in baseCssFiles)
            {
                var baseCss = ThemeResourceCache.GetFileAsync(
                    theme.Id, baseFile, vars, paths, theme.Version)
                    .GetAwaiter().GetResult();
                Log(dbg, "Cache warmed: base CSS (" + (baseCss?.Length ?? 0) + " chars)");
            }

            var addonFiles = theme.GetMatchingAddons(Plugin.ServerVersion);
            foreach (var addon in addonFiles)
            {
                if (string.IsNullOrWhiteSpace(addon.Url)) continue;

                bool active;
                if (!string.IsNullOrEmpty(addon.TriggerVar))
                {
                    active = vars.TryGetValue(addon.TriggerVar, out var tv) &&
                             string.Equals(tv, "true", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    var syntheticKey = "__addon__" + addon.Id;
                    active = vars.TryGetValue(syntheticKey, out var sv) &&
                             string.Equals(sv, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (!active)
                {
                    Log(dbg, "Addon '" + addon.Id + "' skipped (trigger var off)");
                    continue;
                }

                var addonCss = ThemeResourceCache.GetFileAsync(
                    theme.Id, addon, vars, paths, theme.Version)
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
    }

    public class ThemeFileEntry
    {
        public string Type { get; set; } = "css"; // "css", "addon"
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string TriggerVar { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Jellyfin { get; set; } = string.Empty;
        public List<System.Text.Json.JsonElement> Changelog { get; set; } = new List<System.Text.Json.JsonElement>();
    }

    public class ThemeEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
        public string Jellyfin { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public string PreviewUrl { get; set; } = string.Empty;
        public List<string> Screenshots { get; set; } = new List<string>();
        public string SourceUrl { get; set; } = string.Empty;
        public string CssUrl { get; set; }
        public List<ThemeFileEntry> Files { get; set; } = new List<ThemeFileEntry>();
        public List<string> Preconnect { get; set; } = new List<string>();
        public List<ThemeVar> Vars { get; set; } = new List<ThemeVar>();
        public List<ThemeAddon> Addons { get; set; } = new List<ThemeAddon>();
        public bool EditorsChoice { get; set; } = false;
        public List<System.Text.Json.JsonElement> Changelog { get; set; } = new List<System.Text.Json.JsonElement>();

        public List<ThemeFileEntry> GetMatchingCssFiles(Version serverVersion)
        {
            var results = new List<ThemeFileEntry>();
            if (Files != null && Files.Count > 0)
            {
                foreach (var file in Files)
                {
                    if (file == null || string.IsNullOrWhiteSpace(file.Url)) continue;
                    string fType = file.Type?.Trim().ToLowerInvariant() ?? "css";
                    if (fType == "css" || fType == "base")
                    {
                        string constraint = !string.IsNullOrWhiteSpace(file.Jellyfin) ? file.Jellyfin : Jellyfin;
                        if (VersionRangeMatcher.IsCompatible(constraint, serverVersion))
                        {
                            results.Add(file);
                        }
                    }
                }
            }

            if (results.Count == 0 && (Files == null || !HasAnyBaseCss(Files)))
            {
                if (!string.IsNullOrWhiteSpace(CssUrl) && VersionRangeMatcher.IsCompatible(Jellyfin, serverVersion))
                {
                    results.Add(new ThemeFileEntry
                    {
                        Type = "css",
                        Url = CssUrl,
                        Version = Version,
                        Jellyfin = Jellyfin,
                        Changelog = Changelog
                    });
                }
            }

            return results;
        }

        public List<ThemeFileEntry> GetMatchingAddons(Version serverVersion)
        {
            var results = new List<ThemeFileEntry>();
            if (Files != null && Files.Count > 0)
            {
                foreach (var file in Files)
                {
                    if (file == null || string.IsNullOrWhiteSpace(file.Url)) continue;
                    string fType = file.Type?.Trim().ToLowerInvariant() ?? string.Empty;
                    if (fType == "addon")
                    {
                        string constraint = !string.IsNullOrWhiteSpace(file.Jellyfin) ? file.Jellyfin : Jellyfin;
                        if (VersionRangeMatcher.IsCompatible(constraint, serverVersion))
                        {
                            results.Add(file);
                        }
                    }
                }
            }

            if (results.Count == 0 && (Files == null || !HasAnyAddon(Files)))
            {
                if (Addons != null)
                {
                    foreach (var addon in Addons)
                    {
                        if (addon == null || string.IsNullOrWhiteSpace(addon.CssUrl)) continue;
                        if (VersionRangeMatcher.IsCompatible(Jellyfin, serverVersion))
                        {
                            results.Add(new ThemeFileEntry
                            {
                                Type = "addon",
                                Id = addon.Id,
                                Name = addon.Name,
                                Url = addon.CssUrl,
                                TriggerVar = addon.TriggerVar,
                                Version = Version,
                                Jellyfin = Jellyfin
                            });
                        }
                    }
                }
            }

            return results;
        }

        private static bool HasAnyBaseCss(List<ThemeFileEntry> files)
        {
            if (files == null) return false;
            foreach (var f in files)
            {
                if (f == null) continue;
                var t = f.Type?.Trim().ToLowerInvariant();
                if (t == "css" || t == "base") return true;
            }
            return false;
        }

        private static bool HasAnyAddon(List<ThemeFileEntry> files)
        {
            if (files == null) return false;
            foreach (var f in files)
            {
                if (f == null) continue;
                if (f.Type?.Trim().ToLowerInvariant() == "addon") return true;
            }
            return false;
        }
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