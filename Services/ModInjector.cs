using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyFrame.Configuration;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyFrame.Services
{
    public static class ModInjector
    {
        private const string StartMarker = "<!-- JellyFrame-Mods-Start -->";
        private const string EndMarker = "<!-- JellyFrame-Mods-End -->";

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly Regex _modsBlockRegex = new Regex(
            Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
            RegexOptions.Compiled);
        private static readonly Regex _preconnectBlockRegex = new Regex(
            @"<!-- JellyFrame-Mods-Preconnect-Start -->[\s\S]*?<!-- JellyFrame-Mods-Preconnect-End -->\n?",
            RegexOptions.Compiled);
        private static readonly Regex _bodyTagRegex = new Regex(@"(</body>)", RegexOptions.Compiled);
        private static readonly Regex _headTagRegex = new Regex(@"(</head>)", RegexOptions.Compiled);

        private static void Log(bool debug, string msg)
        {
            if (debug) Console.Error.WriteLine("[JellyFrame] " + msg);
        }

        public static string InjectMods(PatchRequestPayload payload)
        {
            try
            {
                string html = payload?.Contents;
                if (string.IsNullOrEmpty(html))
                    return html ?? string.Empty;

                PluginConfiguration config = Plugin.Instance?.Configuration;
                if (config == null)
                    return html;

                bool dbg = config.DebugLogging;

                if (!html.Contains("</body>"))
                    return html;

                var paths = Plugin.Instance?.AppPaths;
                if (paths != null)
                    html = ThemeInjector.InjectTheme(html, config, paths);

                Log(dbg, "InjectMods called. HTML length: " + html.Length);

                if (config.EnabledMods == null || config.EnabledMods.Count == 0)
                {
                    Log(dbg, "No mods enabled — skipping");
                    return html;
                }

                List<ModEntry> allMods = LoadCachedMods(config.CachedMods, dbg);
                if (allMods == null || allMods.Count == 0)
                {
                    Log(dbg, "CachedMods empty — load and save mods in config first");
                    return html;
                }

                Dictionary<string, Dictionary<string, string>> modVars = LoadModVars(config.ModVars, dbg);

                html = _modsBlockRegex.Replace(html, string.Empty);

                html = _preconnectBlockRegex.Replace(html, string.Empty);

                var cssBlocks = new List<string>();
                var jsBlocks = new List<string>();
                var preconnects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string modId in config.EnabledMods)
                {
                    string trimmedId = modId?.Trim();
                    ModEntry mod = allMods.Find(m =>
                        string.Equals(m.Id?.Trim(), trimmedId, StringComparison.OrdinalIgnoreCase));

                    if (mod == null)
                    {
                        Log(dbg, "Mod '" + trimmedId + "' not found in cache");
                        continue;
                    }

                    if (!VersionRangeMatcher.IsCompatible(mod.Jellyfin, Plugin.ServerVersion))
                    {
                        Log(true, $"Mod '{mod.Id}' requires Jellyfin '{mod.Jellyfin}', but server is '{Plugin.ServerVersion}' — skipping frontend injection");
                        continue;
                    }

                    foreach (var origin in mod.Preconnect ?? new List<string>())
                        if (!string.IsNullOrWhiteSpace(origin))
                            preconnects.Add(origin.Trim());

                    Dictionary<string, string> vars = BuildVarMap(mod, modVars);
                    Log(dbg, "Processing mod: '" + mod.Name + "' version=" + mod.Version + " vars=" + vars.Count);

                    var cssFiles = mod.GetMatchingFiles("css", Plugin.ServerVersion);
                    foreach (var cssFile in cssFiles)
                    {
                        Log(dbg, "  -> CSS: " + cssFile.Url);
                        string css = ModResourceCache.GetFileAsync(mod, cssFile, vars, paths)
                            .GetAwaiter().GetResult();

                        if (!string.IsNullOrWhiteSpace(css))
                            cssBlocks.Add(css);
                        else
                            Log(dbg, "  -> CSS fetch failed or empty");
                    }

                    var jsFiles = mod.GetMatchingFiles("js", Plugin.ServerVersion);
                    foreach (var jsFile in jsFiles)
                    {
                        Log(dbg, "  -> JS: " + jsFile.Url);
                        string js = ModResourceCache.GetFileAsync(mod, jsFile, vars, paths)
                            .GetAwaiter().GetResult();

                        if (!string.IsNullOrWhiteSpace(js))
                            jsBlocks.Add(js);
                        else
                            Log(dbg, "  -> JS fetch failed or empty");
                    }
                }

                if (preconnects.Count > 0)
                {
                    var headSb = new StringBuilder();
                    headSb.Append("\n<!-- JellyFrame-Mods-Preconnect-Start -->\n");
                    foreach (var origin in preconnects)
                    {
                        var safe = origin.Replace("&", "&amp;").Replace("\"", "&quot;");
                        headSb.Append("<link rel=\"preconnect\" href=\"").Append(safe).Append("\">\n");
                        headSb.Append("<link rel=\"dns-prefetch\" href=\"").Append(safe).Append("\">\n");
                    }
                    headSb.Append("<!-- JellyFrame-Mods-Preconnect-End -->\n");
                    html = _headTagRegex.Replace(html, headSb.ToString() + "$1");
                    Log(dbg, "Injected " + preconnects.Count + " preconnect origin(s)");
                }

                Log(dbg, "Totals — cssBlocks: " + cssBlocks.Count + ", jsBlocks: " + jsBlocks.Count);

                if (cssBlocks.Count == 0 && jsBlocks.Count == 0)
                {
                    Log(dbg, "Nothing to inject");
                    return html;
                }

                var injection = new StringBuilder();

                if (cssBlocks.Count > 0)
                {
                    injection.AppendLine("<style data-jellyframe-mods=\"1\">");
                    foreach (var css in cssBlocks)
                    {
                        injection.AppendLine(css);
                    }
                    injection.AppendLine("</style>");
                }

                if (jsBlocks.Count > 0)
                {
                    injection.AppendLine("<script data-jellyframe-mods=\"1\">");
                    injection.AppendLine("(function() {");
                    injection.AppendLine("    if (window.__jellyFrameModsLoaded) return;");
                    injection.AppendLine("    window.__jellyFrameModsLoaded = true;");
                    foreach (var js in jsBlocks)
                    {
                        injection.AppendLine(js);
                    }
                    injection.AppendLine("})();");
                    injection.AppendLine("</script>");
                }

                var match = _bodyTagRegex.Match(html);
                if (match.Success)
                {
                    html = _bodyTagRegex.Replace(html,
                        "\n" + StartMarker + "\n" + injection.ToString() + EndMarker + "\n$1", 1);
                    Log(dbg, "Mods injected before </body>");
                }
                else
                {
                    Log(dbg, "No </body> found to inject mods into");
                }

                return html;
            }
            catch (Exception ex)
            {
                Log(true, "InjectMods EXCEPTION: " + ex);
                return payload?.Contents ?? string.Empty;
            }
        }

        private static Dictionary<string, string> BuildVarMap(
            ModEntry mod,
            Dictionary<string, Dictionary<string, string>> modVars)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in mod.Vars ?? new List<ModVar>())
            {
                if (!string.IsNullOrEmpty(v.Key))
                    result[v.Key] = v.Default ?? string.Empty;
            }

            if (modVars.TryGetValue(mod.Id ?? string.Empty, out var saved))
            {
                foreach (var kv in saved)
                    result[kv.Key] = kv.Value;
            }

            return result;
        }

        private static List<ModEntry> LoadCachedMods(string cachedJson, bool dbg)
        {
            if (string.IsNullOrWhiteSpace(cachedJson))
                return new List<ModEntry>();

            try
            {
                return JsonSerializer.Deserialize<List<ModEntry>>(cachedJson, JsonOpts)
                    ?? new List<ModEntry>();
            }
            catch (Exception ex)
            {
                Log(dbg, "Failed to deserialize CachedMods: " + ex.Message);
                return new List<ModEntry>();
            }
        }

        private static Dictionary<string, Dictionary<string, string>> LoadModVars(string modVarsJson, bool dbg)
        {
            if (string.IsNullOrWhiteSpace(modVarsJson) || modVarsJson == "{}")
                return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(modVarsJson, JsonOpts)
                    ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log(dbg, "Failed to deserialize ModVars: " + ex.Message);
                return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public class ModFileEntry
    {
        public string Type { get; set; } = string.Empty; // "js", "css", "server", "serverjs"
        public string Url { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Jellyfin { get; set; } = string.Empty;
        public List<System.Text.Json.JsonElement> Changelog { get; set; } = new List<System.Text.Json.JsonElement>();
    }

    public class ModVar
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Default { get; set; } = string.Empty;
        public bool? AllowGradient { get; set; } = null;
        public string TrueValue { get; set; } = "true";
        public string FalseValue { get; set; } = "false";
    }

    public class ModEntry
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

        public string JsUrl { get; set; }

        public string ServerJs { get; set; } = string.Empty;

        public List<ModFileEntry> Files { get; set; } = new List<ModFileEntry>();

        public List<string> Permissions { get; set; } = new List<string>();

        public List<string> Requires { get; set; } = new List<string>();

        public List<string> Preconnect { get; set; } = new List<string>();

        public List<ModVar> Vars { get; set; } = new List<ModVar>();
        public bool EditorsChoice { get; set; } = false;
        public List<System.Text.Json.JsonElement> Changelog { get; set; } = new List<System.Text.Json.JsonElement>();

        /// <summary>
        /// If true, the mod runtime will be automatically restarted after a crash
        /// using an exponential backoff (30s → 60s → 120s → 120s …).
        /// Defaults to false — crashes leave the mod in a stopped state until
        /// the server restarts or an admin manually re-enables the mod.
        /// </summary>
        public bool RestartOnCrash { get; set; } = false;

        /// <summary>
        /// Resolves all files of targetType ("css", "js", "server") compatible with the running Jellyfin server.
        /// Falls back to legacy CssUrl, JsUrl, or ServerJs if Files array is omitted or has no matching entries.
        /// </summary>
        public List<ModFileEntry> GetMatchingFiles(string targetType, Version serverVersion)
        {
            var results = new List<ModFileEntry>();
            string normalizedTarget = targetType?.Trim().ToLowerInvariant() ?? string.Empty;
            bool isServerTarget = normalizedTarget == "server" || normalizedTarget == "serverjs";

            if (Files != null && Files.Count > 0)
            {
                foreach (var file in Files)
                {
                    if (file == null || string.IsNullOrWhiteSpace(file.Url))
                        continue;

                    string fType = file.Type?.Trim().ToLowerInvariant() ?? string.Empty;
                    bool typeMatches = isServerTarget
                        ? (fType == "server" || fType == "serverjs")
                        : (fType == normalizedTarget);

                    if (typeMatches)
                    {
                        string constraint = !string.IsNullOrWhiteSpace(file.Jellyfin) ? file.Jellyfin : Jellyfin;
                        if (VersionRangeMatcher.IsCompatible(constraint, serverVersion))
                        {
                            results.Add(file);
                        }
                    }
                }
            }

            // Fallback to legacy fields if no matching files of targetType were found in Files
            if (results.Count == 0 && (Files == null || !HasAnyFileOfType(Files, normalizedTarget, isServerTarget)))
            {
                if (VersionRangeMatcher.IsCompatible(Jellyfin, serverVersion))
                {
                    if (normalizedTarget == "css" && !string.IsNullOrWhiteSpace(CssUrl))
                    {
                        results.Add(new ModFileEntry
                        {
                            Type = "css",
                            Url = CssUrl,
                            Version = Version,
                            Jellyfin = Jellyfin,
                            Changelog = Changelog
                        });
                    }
                    else if (normalizedTarget == "js" && !string.IsNullOrWhiteSpace(JsUrl))
                    {
                        results.Add(new ModFileEntry
                        {
                            Type = "js",
                            Url = JsUrl,
                            Version = Version,
                            Jellyfin = Jellyfin,
                            Changelog = Changelog
                        });
                    }
                    else if (isServerTarget && !string.IsNullOrWhiteSpace(ServerJs))
                    {
                        results.Add(new ModFileEntry
                        {
                            Type = "server",
                            Url = ServerJs,
                            Version = Version,
                            Jellyfin = Jellyfin,
                            Changelog = Changelog
                        });
                    }
                }
            }

            return results;
        }

        private static bool HasAnyFileOfType(List<ModFileEntry> files, string normalizedTarget, bool isServerTarget)
        {
            if (files == null) return false;
            foreach (var f in files)
            {
                if (f == null) continue;
                string fType = f.Type?.Trim().ToLowerInvariant() ?? string.Empty;
                if (isServerTarget && (fType == "server" || fType == "serverjs")) return true;
                if (!isServerTarget && fType == normalizedTarget) return true;
            }
            return false;
        }
    }
}