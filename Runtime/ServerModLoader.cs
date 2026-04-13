using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyFrame.Services;
using Jellyfin.Plugin.JellyFrame.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public sealed class ServerModLoader : IDisposable
    {
        private readonly ConcurrentDictionary<string, JsRuntime> _runtimes
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly ILibraryManager _library;
        private readonly IUserDataManager _userData;
        private readonly IUserManager _users;
        private readonly ISessionManager _sessions;
        private readonly ISubtitleManager _subtitles;
        private readonly IMediaEncoder _encoder;
        private readonly IPlaylistManager _playlists;
        private readonly IDtoService _dto;
        private readonly ICollectionManager _collections;
        private readonly IProviderManager _providers;
        private readonly IActivityManager _activity;
        private readonly ITaskManager _tasks;
        private readonly IDeviceManager _devices;
        private readonly ITVSeriesManager _tvSeries;
        private readonly ILiveTvManager _liveTv;
        private readonly IMediaSourceManager _mediaSources;
        private readonly MediaBrowser.Model.IO.IFileSystem _fileSystem;
        private readonly IApplicationPaths _paths;
        private readonly ILogger<ServerModLoader> _logger;

        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private readonly Timer _gcTimer;
        private ModFileWatcher _watcher;

        private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };
        private bool _disposed;

        private static readonly TimeSpan GcInterval = TimeSpan.FromMinutes(10);

        public ServerModLoader(
            ILibraryManager library,
            IUserDataManager userData,
            IUserManager users,
            ISessionManager sessions,
            ISubtitleManager subtitles,
            IMediaEncoder encoder,
            IPlaylistManager playlists,
            IDtoService dto,
            ICollectionManager collections,
            IProviderManager providers,
            IActivityManager activity,
            ITaskManager tasks,
            IDeviceManager devices,
            ITVSeriesManager tvSeries,
            ILiveTvManager liveTv,
            IMediaSourceManager mediaSources,
            MediaBrowser.Model.IO.IFileSystem fileSystem,
            IApplicationPaths paths,
            ILogger<ServerModLoader> logger)
        {
            _library = library;
            _userData = userData;
            _users = users;
            _sessions = sessions;
            _subtitles = subtitles;
            _encoder = encoder;
            _playlists = playlists;
            _dto = dto;
            _collections = collections;
            _providers = providers;
            _activity = activity;
            _tasks = tasks;
            _devices = devices;
            _tvSeries = tvSeries;
            _liveTv = liveTv;
            _mediaSources = mediaSources;
            _fileSystem = fileSystem;
            _paths = paths;
            _logger = logger;

            _gcTimer = new Timer(_ => RunPeriodicGc(), null, GcInterval, GcInterval);
        }

        public async Task LoadModsAsync(
            IEnumerable<ModEntry> mods,
            IEnumerable<string> enabledIds,
            Dictionary<string, Dictionary<string, string>> modVars,
            bool forceReload = false,
            CancellationToken cancellationToken = default)
        {
            await _loadLock.WaitAsync(cancellationToken);
            try
            {
                var enabledSet = new HashSet<string>(enabledIds, StringComparer.OrdinalIgnoreCase);

                foreach (var key in _runtimes.Keys)
                    if (!enabledSet.Contains(key))
                        await DisableModCoreAsync(key);

                var serverMods = new List<ModEntry>();
                foreach (var m in mods)
                    if (enabledSet.Contains(m.Id) && !string.IsNullOrWhiteSpace(m.ServerJs))
                        serverMods.Add(m);

                var modIndex = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in serverMods) modIndex[m.Id] = m;

                var loadOrder = TopologicalSort(serverMods, modIndex, enabledSet);

                foreach (var mod in loadOrder)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    bool alreadyLoaded = _runtimes.TryGetValue(mod.Id, out var existing) && existing.IsLoaded;
                    if (alreadyLoaded && !forceReload) continue;

                    try { await LoadModCoreAsync(mod, modVars, cancellationToken); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[JellyFrame] Failed to load server mod: {Id}", mod.Id);
                    }
                }
            }
            finally { _loadLock.Release(); }
        }

        private async Task LoadModCoreAsync(
            ModEntry mod,
            Dictionary<string, Dictionary<string, string>> modVars,
            CancellationToken ct)
        {
            if (_runtimes.TryRemove(mod.Id, out var old))
                old.Dispose();

            _logger.LogInformation("[JellyFrame] Fetching server script for {Id} from {Url}", mod.Id, mod.ServerJs);

            string script;
            try
            {
                script = await ModResourceCache.GetServerJsAsync(mod, _paths);
                if (string.IsNullOrWhiteSpace(script))
                {
                    _logger.LogError("[JellyFrame] Server script empty or fetch failed for {Id}", mod.Id);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] Failed to fetch server script for {Id}", mod.Id);
                return;
            }

            var vars = BuildVarMap(mod, modVars);
            var log = new LogSurface(_logger, mod.Id);
            var cache = new CacheSurface();
            var store = new StoreSurface(mod.Id, _paths);
            var userStore = new UserStoreSurface(mod.Id, _paths);
            var kv = new KvSurface(mod.Id, _paths);
            var http = new HttpSurface();
            var routes = new RoutesSurface(mod.Id);
            var scheduler = new SchedulerSurface(mod.Id, _logger);
            var bus = new EventBusSurface(mod.Id, _logger);
            var webhooks = new WebhookSurface(mod.Id, _logger);
            var rpc = new RpcSurface(mod.Id, _logger);
            var perms = new PermissionSurface(mod.Id, mod.Permissions);
            var jellyfin = new JellyfinSurface(
                _library, _userData, _users, _sessions,
                _subtitles, _encoder, _playlists, _dto,
                _collections, _providers, _activity, _tasks, _devices,
                _tvSeries, _liveTv, _mediaSources, _fileSystem, _logger);

            foreach (var u in PermissionSurface.UnknownPermissions(mod.Permissions))
                _logger.LogWarning("[JellyFrame] Mod '{Id}' declared unknown permission '{P}'", mod.Id, u);

            var context = new JellyFrameContext(
                mod.Id, vars, jellyfin, routes, store, userStore,
                kv, cache, http, log, scheduler, bus, webhooks, rpc, perms);
            var runtime = new JsRuntime(context, _logger);

            runtime.LoadScript(script);
            _runtimes[mod.Id] = runtime;

            _logger.LogInformation("[JellyFrame] Server mod loaded: {Id} — {Routes} route(s)",
                mod.Id, routes.Routes.Count);
        }

        public async Task EnableModAsync(ModEntry mod, Dictionary<string, Dictionary<string, string>> modVars)
        {
            if (string.IsNullOrWhiteSpace(mod.ServerJs)) return;
            await _loadLock.WaitAsync();
            try { await LoadModCoreAsync(mod, modVars, CancellationToken.None); }
            finally { _loadLock.Release(); }
        }

        public async Task DisableModAsync(string modId)
        {
            await _loadLock.WaitAsync();
            try { await DisableModCoreAsync(modId); }
            finally { _loadLock.Release(); }
        }

        private async Task DisableModCoreAsync(string modId)
        {
            if (!_runtimes.TryRemove(modId, out var runtime)) return;
            runtime.Dispose();
            _logger.LogInformation("[JellyFrame] Disabled server mod: {Id}", modId);
            await Task.Run(() =>
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            });
        }

        public async Task ReloadModAsync(ModEntry mod, Dictionary<string, Dictionary<string, string>> modVars)
        {
            await _loadLock.WaitAsync();
            try { await LoadModCoreAsync(mod, modVars, CancellationToken.None); }
            finally { _loadLock.Release(); }
        }

        public bool DispatchWebhook(string modId, string name, string body,
            System.Collections.Generic.IDictionary<string, string> headers)
        {
            if (!_runtimes.TryGetValue(modId, out var runtime) || !runtime.IsLoaded)
                return false;
            return runtime.DispatchWebhook(name, body, headers);
        }

        public async Task<bool> TryHandleRequestAsync(HttpContext context)
        {
            foreach (var runtime in _runtimes.Values)
            {
                if (!runtime.IsLoaded) continue;
                try { if (await runtime.HandleRequestAsync(context)) return true; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyFrame] Route handler error in mod {Id}", runtime.ModId);
                }
            }
            return false;
        }

        public async Task FireEventAsync(string eventName, object data)
        {
            foreach (var runtime in _runtimes.Values)
            {
                if (!runtime.IsLoaded) continue;
                try { await runtime.FireEventAsync(eventName, data); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyFrame] Event error in mod {Id}", runtime.ModId);
                }
            }
        }

        private void RunPeriodicGc()
        {
            if (_disposed) return;
            _logger.LogDebug("[JellyFrame] Periodic GC — {Count} mod(s) loaded", _runtimes.Count);
            foreach (var runtime in _runtimes.Values)
                try { runtime.ForceGc(); } catch { }
            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }

        public void StartWatcher(string cacheDir,
            Func<string, Task> getModEntryAndVars)
        {
            _watcher?.Dispose();

            Func<string, Task> onCssChanged = modId =>
            {
                _logger.LogInformation(
                    "[JellyFrame] CSS hot-reload: invalidating CSS cache for '{Id}'", modId);
                ModResourceCache.InvalidateType(modId, "css", _paths);
                return Task.CompletedTask;
            };

            _watcher = new ModFileWatcher(cacheDir, getModEntryAndVars, _logger, onCssChanged);
            _logger.LogInformation("[JellyFrame] Hot-reload watcher started on '{Dir}' (JS + CSS)", cacheDir);
        }

        public void StopWatcher()
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        public IEnumerable<string> LoadedModIds => _runtimes.Keys;
        public bool IsModLoaded(string modId) => _runtimes.TryGetValue(modId, out var r) && r.IsLoaded;
        public int LoadedCount => _runtimes.Count;
        public bool WatcherActive => _watcher != null;

        public IEnumerable<ModHealthSnapshot> GetHealthSnapshots()
        {
            var list = new List<ModHealthSnapshot>();
            foreach (var kv in _runtimes)
                list.Add(kv.Value.GetHealthSnapshot());
            return list;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _logger.LogInformation("[JellyFrame] ServerModLoader shutting down — {Count} mod(s)", _runtimes.Count);
            _gcTimer?.Dispose();
            _watcher?.Dispose();
            foreach (var kv in _runtimes)
                try { kv.Value.Dispose(); }
                catch (Exception ex) { _logger.LogError(ex, "[JellyFrame] Error disposing {Id}", kv.Key); }
            _runtimes.Clear();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            _loadLock.Dispose();
            _logger.LogInformation("[JellyFrame] ServerModLoader shutdown complete");
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

        private List<ModEntry> TopologicalSort(
            List<ModEntry> mods,
            Dictionary<string, ModEntry> index,
            HashSet<string> enabledSet)
        {
            var sorted = new List<ModEntry>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(ModEntry mod)
            {
                if (skip.Contains(mod.Id) || visited.Contains(mod.Id)) return;

                if (inStack.Contains(mod.Id))
                {
                    _logger.LogError(
                        "[JellyFrame] Circular dependency detected at '{Id}' — skipping", mod.Id);
                    skip.Add(mod.Id);
                    return;
                }

                inStack.Add(mod.Id);

                foreach (var dep in mod.Requires ?? new List<string>())
                {
                    if (!enabledSet.Contains(dep))
                    {
                        _logger.LogError(
                            "[JellyFrame] Mod '{Id}' requires '{Dep}' which is not enabled — skipping '{Id}'",
                            mod.Id, dep, mod.Id);
                        skip.Add(mod.Id);
                        inStack.Remove(mod.Id);
                        return;
                    }
                    if (index.TryGetValue(dep, out var depMod))
                        Visit(depMod);

                    if (skip.Contains(dep))
                    {
                        _logger.LogError(
                            "[JellyFrame] Mod '{Id}' dependency '{Dep}' failed to load — skipping '{Id}'",
                            mod.Id, dep, mod.Id);
                        skip.Add(mod.Id);
                        inStack.Remove(mod.Id);
                        return;
                    }
                }

                inStack.Remove(mod.Id);
                visited.Add(mod.Id);
                sorted.Add(mod);
            }

            foreach (var mod in mods) Visit(mod);
            return sorted;
        }
    }

    public class ModHealthSnapshot
    {
        public string ModId { get; set; }
        public bool IsLoaded { get; set; }
        public int RouteCount { get; set; }
        public int SchedulerTasks { get; set; }
        public int CacheEntries { get; set; }
        public int StoreKeys { get; set; }
        public int UserStoreUsers { get; set; }
        public string[] RegisteredWebhooks { get; set; }
        public string[] RpcMethods { get; set; }
        public DateTime? LoadedAt { get; set; }
    }
}
