using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyFrame.Configuration;
using Jellyfin.Plugin.JellyFrame.Runtime;
using Jellyfin.Plugin.JellyFrame.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
    {
        private const int CurrentConfigVersion = 1;

        public override string Name => "JellyFrame";
        public override Guid Id => Guid.Parse("d4e5f6a7-b8c9-0123-defa-456789012345");
        public override string Description => "JellyFrame — a complete customization and extension framework for Jellyfin.";

        public static Plugin Instance { get; private set; }
        public IServiceProvider ServiceProvider { get; private set; }
        public ServerModLoader ModLoader { get; private set; }
        public IApplicationPaths AppPaths { get; private set; }

        private readonly ILogger<Plugin> _logger;
        private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };
        private readonly MediaBrowser.Controller.Library.ILibraryManager _libraryManager;
        private readonly MediaBrowser.Controller.Session.ISessionManager _sessionManager;
        private Runtime.JellyfinEventSurface _eventSurface;
        private bool _disposed;

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogger<Plugin> logger,
            IServiceProvider serviceProvider,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            IUserManager userManager,
            ISessionManager sessionManager,
            ISubtitleManager subtitleManager,
            IMediaEncoder mediaEncoder,
            IPlaylistManager playlistManager,
            IDtoService dtoService,
            ILoggerFactory loggerFactory)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            ServiceProvider = serviceProvider;
            AppPaths = applicationPaths;
            _logger = logger;
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;

            ModLoader = new ServerModLoader(
                libraryManager,
                userDataManager,
                userManager,
                sessionManager,
                subtitleManager,
                mediaEncoder,
                playlistManager,
                dtoService,
                applicationPaths,
                loggerFactory.CreateLogger<ServerModLoader>());

            MigrateConfig();
            ConfigurationChanged += OnConfigurationChanged;
            _ = InitServerModsAsync();
        }

        private string _prevCachedMods = string.Empty;
        private string _prevModVars = "{}";
        private List<string> _prevEnabledMods = new List<string>();
        private string _prevActiveTheme = string.Empty;
        private string _prevThemeVars = "{}";

        private void OnConfigurationChanged(object sender, BasePluginConfiguration baseConfig)
        {
            var config = (PluginConfiguration)baseConfig;
            var paths = AppPaths;

            var prevMods = ParseModList(_prevCachedMods);
            var currMods = ParseModList(config.CachedMods);

            var prevVars = ParseModVars(_prevModVars);
            var currVars = ParseModVars(config.ModVars);

            var prevEnabled = new HashSet<string>(_prevEnabledMods ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            var currEnabled = new HashSet<string>(config.EnabledMods ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var mod in currMods.Values)
            {
                bool wasEnabled = prevEnabled.Contains(mod.Id);
                bool isEnabled = currEnabled.Contains(mod.Id);

                prevMods.TryGetValue(mod.Id, out var prevMod);
                bool versionChanged = prevMod == null ||
                    !string.Equals(prevMod.Version, mod.Version, StringComparison.Ordinal);

                if (versionChanged)
                {
                    _logger.LogInformation(
                        "[JellyFrame] Version changed for '{Id}' ({Prev} → {Curr}) — invalidating cache",
                        mod.Id, prevMod?.Version ?? "new", mod.Version);
                    ModResourceCache.InvalidateMod(mod.Id, paths);
                    continue;
                }

                currVars.TryGetValue(mod.Id, out var currModVars);
                prevVars.TryGetValue(mod.Id, out var prevModVars);
                if (VarsChanged(prevModVars, currModVars))
                {
                    _logger.LogInformation(
                        "[JellyFrame] Vars changed for '{Id}' — invalidating compiled cache", mod.Id);
                    ModResourceCache.InvalidateCompiled(mod.Id, paths);
                }

                if (!wasEnabled && isEnabled)
                {
                    _logger.LogInformation(
                        "[JellyFrame] Mod '{Id}' enabled — invalidating compiled cache", mod.Id);
                    ModResourceCache.InvalidateCompiled(mod.Id, paths);
                }

                if (wasEnabled && !isEnabled)
                {
                    _logger.LogInformation(
                        "[JellyFrame] Mod '{Id}' disabled — invalidating compiled cache", mod.Id);
                    ModResourceCache.InvalidateCompiled(mod.Id, paths);
                }
            }

            _prevCachedMods = config.CachedMods ?? string.Empty;
            _prevModVars = config.ModVars ?? "{}";
            _prevEnabledMods = config.EnabledMods ?? new List<string>();

            var prevActiveTheme = _prevActiveTheme;
            var prevThemeVars = _prevThemeVars;
            _prevActiveTheme = config.ActiveTheme ?? string.Empty;
            _prevThemeVars = config.ThemeVars ?? "{}";

            if (!string.Equals(prevActiveTheme, config.ActiveTheme, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(prevActiveTheme))
                {
                    _logger.LogInformation(
                        "[JellyFrame] Active theme changed — invalidating previous theme cache '{Id}'",
                        prevActiveTheme);
                    Services.ThemeResourceCache.InvalidateTheme(prevActiveTheme, paths);
                }
                if (!string.IsNullOrWhiteSpace(config.ActiveTheme))
                    Services.ThemeResourceCache.InvalidateTheme(config.ActiveTheme, paths);
            }
            else if (!string.Equals(prevThemeVars, config.ThemeVars, StringComparison.Ordinal)
                     && !string.IsNullOrWhiteSpace(config.ActiveTheme))
            {
                _logger.LogInformation(
                    "[JellyFrame] Theme vars changed — invalidating compiled cache for '{Id}'",
                    config.ActiveTheme);
                Services.ThemeResourceCache.InvalidateCompiled(config.ActiveTheme, paths);
            }

            _ = ReloadServerModsAsync();
        }

        private Dictionary<string, ModEntry> ParseModList(string json)
        {
            var result = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var list = JsonSerializer.Deserialize<List<ModEntry>>(json, _jsonOpts);
                if (list != null)
                    foreach (var m in list)
                        if (!string.IsNullOrEmpty(m.Id))
                            result[m.Id] = m;
            }
            catch { }
            return result;
        }

        private Dictionary<string, Dictionary<string, string>> ParseModVars(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
                return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, _jsonOpts)
                    ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static bool VarsChanged(
            Dictionary<string, string> prev,
            Dictionary<string, string> curr)
        {
            prev ??= new Dictionary<string, string>();
            curr ??= new Dictionary<string, string>();
            if (prev.Count != curr.Count) return true;
            foreach (var kv in curr)
            {
                if (!prev.TryGetValue(kv.Key, out var prevVal)) return true;
                if (!string.Equals(prevVal, kv.Value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private async Task InitServerModsAsync()
        {
            try
            {
                await LoadServerModsFromConfig(forceReload: false);

                var cacheDir = System.IO.Path.Combine(AppPaths.DataPath, "JellyFrame", "mods");
                ModLoader.StartWatcher(cacheDir, HotReloadModAsync);

                _eventSurface = new Runtime.JellyfinEventSurface(
                    _libraryManager,
                    _sessionManager,
                    _logger,
                    (eventName, data) => ModLoader.FireEventAsync(eventName, data));
            }
            catch (Exception ex) { _logger.LogError(ex, "[JellyFrame] Startup mod load failed"); }
        }

        private async Task HotReloadModAsync(string safeModId)
        {
            var config = Configuration;
            if (string.IsNullOrWhiteSpace(config.CachedMods)) return;

            List<ModEntry> mods;
            try
            {
                mods = JsonSerializer.Deserialize<List<ModEntry>>(config.CachedMods, _jsonOpts)
                       ?? new List<ModEntry>();
            }
            catch { return; }

            ModEntry target = null;
            foreach (var m in mods)
            {
                if (string.IsNullOrEmpty(m.ServerJs)) continue;
                var safe = System.Text.RegularExpressions.Regex.Replace(
                    m.Id ?? "", @"[^\w\-.]", "_");
                if (string.Equals(safe, safeModId, StringComparison.OrdinalIgnoreCase))
                {
                    target = m;
                    break;
                }
            }

            if (target == null)
            {
                _logger.LogWarning("[JellyFrame] Hot-reload: no mod found for safe ID '{Safe}'", safeModId);
                return;
            }

            var enabled = new System.Collections.Generic.HashSet<string>(
                config.EnabledMods ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            if (!enabled.Contains(target.Id)) return;

            Dictionary<string, Dictionary<string, string>> modVars;
            try
            {
                modVars = string.IsNullOrWhiteSpace(config.ModVars) || config.ModVars == "{}"
                    ? new Dictionary<string, Dictionary<string, string>>()
                    : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                        config.ModVars, _jsonOpts)
                      ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch { modVars = new Dictionary<string, Dictionary<string, string>>(); }

            _logger.LogInformation("[JellyFrame] Hot-reloading mod '{Id}'", target.Id);
            await ModLoader.ReloadModAsync(target, modVars);
        }

        private async Task ReloadServerModsAsync()
        {
            try { await LoadServerModsFromConfig(forceReload: true); }
            catch (Exception ex) { _logger.LogError(ex, "[JellyFrame] Config reload failed"); }
        }

        private async Task LoadServerModsFromConfig(bool forceReload)
        {
            var config = Configuration;
            if (string.IsNullOrWhiteSpace(config.CachedMods)) return;

            List<ModEntry> mods;
            try
            {
                mods = JsonSerializer.Deserialize<List<ModEntry>>(config.CachedMods, _jsonOpts)
                       ?? new List<ModEntry>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] Failed to deserialize CachedMods");
                return;
            }
            Dictionary<string, Dictionary<string, string>> modVars;
            try
            {
                modVars = string.IsNullOrWhiteSpace(config.ModVars) || config.ModVars == "{}"
                    ? new Dictionary<string, Dictionary<string, string>>()
                    : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                        config.ModVars, _jsonOpts)
                      ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch { modVars = new Dictionary<string, Dictionary<string, string>>(); }

            await ModLoader.LoadModsAsync(
                mods,
                config.EnabledMods ?? new List<string>(),
                modVars,
                forceReload);
        }

        private void MigrateConfig()
        {
            bool dirty = false;
            for (int v = Configuration.ConfigVersion + 1; v <= CurrentConfigVersion; v++)
            {
                switch (v)
                {
                    case 1:
                        if (Configuration.EnabledMods == null)
                            Configuration.EnabledMods = new List<string>();
                        if (string.IsNullOrWhiteSpace(Configuration.CachedMods))
                            Configuration.CachedMods = string.Empty;
                        if (string.IsNullOrWhiteSpace(Configuration.ModVars))
                            Configuration.ModVars = "{}";
                        if (string.IsNullOrWhiteSpace(Configuration.ThemesUrl))
                            Configuration.ThemesUrl = string.Empty;
                        if (string.IsNullOrWhiteSpace(Configuration.ActiveTheme))
                            Configuration.ActiveTheme = string.Empty;
                        if (string.IsNullOrWhiteSpace(Configuration.CachedThemes))
                            Configuration.CachedThemes = string.Empty;
                        if (string.IsNullOrWhiteSpace(Configuration.ThemeVars))
                            Configuration.ThemeVars = "{}";
                        break;
                }
                Configuration.ConfigVersion = v;
                dirty = true;
            }
            if (dirty) SaveConfiguration();
        }

        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo
            {
                Name                 = "JellyFrameMods",
                DisplayName          = "Mods",
                EnableInMainMenu     = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.modsPage.html"
            },
            new PluginPageInfo
            {
                Name                 = "JellyFrameThemes",
                DisplayName          = "Themes",
                EnableInMainMenu     = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.themesPage.html"
            }
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ConfigurationChanged -= OnConfigurationChanged;
            _eventSurface?.Dispose();
            ModLoader?.StopWatcher();
            ModLoader?.Dispose();
            _logger.LogInformation("[JellyFrame] Plugin disposed");
        }
    }
}