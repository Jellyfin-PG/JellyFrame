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
        private const string EndMarker   = "<!-- JellyFrame-Mods-End -->";

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

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

                html = Regex.Replace(
                    html,
                    Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
                    string.Empty);

                var cssBlocks = new List<string>();
                var jsBlocks  = new List<string>();

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

                    Dictionary<string, string> vars = BuildVarMap(mod, modVars);
                    Log(dbg, "Processing mod: '" + mod.Name + "' version=" + mod.Version + " vars=" + vars.Count);

                    if (!string.IsNullOrWhiteSpace(mod.CssUrl))
                    {
                        Log(dbg, "  -> CSS: " + mod.CssUrl);
                        string css = ModResourceCache.GetCssAsync(mod, vars, paths)
                            .GetAwaiter().GetResult();

                        if (!string.IsNullOrWhiteSpace(css))
                            cssBlocks.Add(css);
                        else
                            Log(dbg, "  -> CSS fetch failed or empty");
                    }

                    if (!string.IsNullOrWhiteSpace(mod.JsUrl))
                    {
                        Log(dbg, "  -> JS: " + mod.JsUrl);
                        string js = ModResourceCache.GetJsAsync(mod, vars, paths)
                            .GetAwaiter().GetResult();

                        if (!string.IsNullOrWhiteSpace(js))
                            jsBlocks.Add(js);
                        else
                            Log(dbg, "  -> JS fetch failed or empty");
                    }
                }

                Log(dbg, "Totals — cssBlocks: " + cssBlocks.Count + ", jsBlocks: " + jsBlocks.Count);

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

                if (!string.IsNullOrEmpty(injection.ToString().Trim()))
                {
                    string block  = "\n" + StartMarker + "\n" + injection.ToString() + EndMarker + "\n";
                    string before = html;
                    html = Regex.Replace(html, @"(</body>)", block + "$1");
                    Log(dbg, html == before
                        ? "WARNING: </body> not found — injection failed"
                        : "Injected successfully. New length: " + html.Length);
                }
                else
                {
                    Log(dbg, "Nothing to inject");
                }

                return html;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[JellyFrame] EXCEPTION in InjectMods: " + ex);
                return payload?.Contents ?? string.Empty;
            }
        }

        private static Dictionary<string, string> BuildVarMap(
            ModEntry mod,
            Dictionary<string, Dictionary<string, string>> modVars)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in mod.Vars ?? new List<ModVar>())
                if (!string.IsNullOrEmpty(v.Key))
                    result[v.Key] = v.Default ?? string.Empty;

            if (modVars.TryGetValue(mod.Id ?? string.Empty, out var saved))
                foreach (var kv in saved)
                    result[kv.Key] = kv.Value;

            return result;
        }

        private static List<ModEntry> LoadCachedMods(string json, bool dbg)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<List<ModEntry>>(json, JsonOpts);
            }
            catch (Exception ex)
            {
                Log(dbg, "Failed to deserialize CachedMods: " + ex.Message);
                return null;
            }
        }

        private static Dictionary<string, Dictionary<string, string>> LoadModVars(string json, bool dbg)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
                return new Dictionary<string, Dictionary<string, string>>();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, JsonOpts)
                    ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch (Exception ex)
            {
                Log(dbg, "Failed to deserialize ModVars: " + ex.Message);
                return new Dictionary<string, Dictionary<string, string>>();
            }
        }
    }

    public class ModVar
    {
        public string Key           { get; set; } = string.Empty;
        public string Name          { get; set; } = string.Empty;
        public string Description   { get; set; } = string.Empty;
        public string Default       { get; set; } = string.Empty;
        public bool?  AllowGradient { get; set; } = null;
        public string TrueValue     { get; set; } = "true";
        public string FalseValue    { get; set; } = "false";
    }

    public class ModEntry
    {
        public string       Id          { get; set; }
        public string       Name        { get; set; }
        public string       Author      { get; set; }
        public string       Description { get; set; }
        public string       Version     { get; set; }
        public string       Jellyfin    { get; set; } = string.Empty;
        public List<string> Tags        { get; set; } = new List<string>();
        public string       PreviewUrl  { get; set; } = string.Empty;
        public string       SourceUrl   { get; set; } = string.Empty;

        public string CssUrl    { get; set; }

        public string JsUrl     { get; set; }

        public string ServerJs  { get; set; } = string.Empty;

        public List<string> Permissions { get; set; } = new List<string>();

        public List<string> Requires    { get; set; } = new List<string>();

        public List<ModVar> Vars { get; set; } = new List<ModVar>();
    }
}
