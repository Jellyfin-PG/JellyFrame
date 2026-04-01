using System;
using System.Threading.Tasks;
using Jint;
using Jint.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public sealed class JsRuntime : IDisposable
    {
        private Engine _engine;
        private JellyFrameContext _context;
        private readonly ILogger _logger;
        private bool _disposed;
        private bool _loaded;
        private readonly object _lock = new();

        public string ModId => _context?.ModId;
        public RoutesSurface Routes => _context?.Routes;
        public GatedJellyfinSurface Jellyfin => _context?.Jellyfin;
        public bool IsLoaded => _loaded && !_disposed;
        public Engine Engine => _engine;
        public DateTime? LoadedAt { get; private set; }

        public JsRuntime(JellyFrameContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _engine = BuildEngine();
            RegisterGlobals();

            _context._rawRoutes?.SetEngine(_engine);
            _context._rawScheduler?.SetEngine(_engine);
            _context._rawBus?.SetEngine(_engine);
            _context._rawWebhooks?.SetEngine(_engine);
            _context._rawRpc?.SetEngine(_engine);
        }

        private Engine BuildEngine() => new Engine(options =>
        {
            options.Strict();
            options.LimitMemory(64 * 1024 * 1024);
            options.TimeoutInterval(TimeSpan.FromSeconds(30));
            options.MaxStatements(1_000_000);
        });

        private void RegisterGlobals()
        {
            _engine.SetValue("__jfCtx", _context);
            _engine.SetValue("console", new JsConsole(_logger, _context.ModId));
            _engine.Execute(
                "var jf = {" +
                "  vars:      __jfCtx.Vars," +
                "  log:       __jfCtx.Log," +
                "  cache:     __jfCtx.Cache," +
                "  perms:     __jfCtx.Perms," +
                "  routes:    __jfCtx.Routes," +
                "  http:      __jfCtx.Http," +
                "  jellyfin:  __jfCtx.Jellyfin," +
                "  store:     __jfCtx.Store," +
                "  userStore: __jfCtx.UserStore," +
                "  scheduler: __jfCtx.Scheduler," +
                "  bus:       __jfCtx.Bus," +
                "  webhooks:  __jfCtx.Webhooks," +
                "  rpc:       __jfCtx.Rpc," +
                "  onStart:   function(fn) { __jfCtx.OnStart(fn); }," +
                "  onStop:    function(fn)  { __jfCtx.OnStop(fn); }" +
                "};"
            );
        }

        public void LoadScript(string script)
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                try
                {
                    _engine.Execute(script);
                    _context.InvokeStart();
                    _loaded = true;
                    LoadedAt = DateTime.UtcNow;
                    _logger.LogInformation("[JellyFrame] Mod {ModId} loaded — {Routes} route(s)",
                        _context.ModId, _context._rawRoutes?.Routes.Count ?? 0);
                }
                catch (MemoryLimitExceededException)
                {
                    _logger.LogError("[JellyFrame] Mod {ModId} exceeded 64 MB memory limit", _context.ModId);
                    DisposeEngine();
                    throw;
                }
                catch (TimeoutException)
                {
                    _logger.LogError("[JellyFrame] Mod {ModId} timed out during load (>30s)", _context.ModId);
                    DisposeEngine();
                    throw;
                }
                catch (JavaScriptException ex)
                {
                    _logger.LogError("[JellyFrame] Script error in mod {ModId} at line {Line}: {Msg}",
                        _context.ModId, ex.Location.Start.Line, ex.Message);
                    DisposeEngine();
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyFrame] Failed to load server script for mod {ModId}", _context.ModId);
                    DisposeEngine();
                    throw;
                }
            }
        }

        public ModHealthSnapshot GetHealthSnapshot() => new ModHealthSnapshot
        {
            ModId = ModId,
            IsLoaded = IsLoaded,
            RouteCount = _context?._rawRoutes?.Routes?.Count ?? 0,
            SchedulerTasks = _context?._rawScheduler?.Count ?? 0,
            CacheEntries = _context?.Cache?.Count ?? 0,
            StoreKeys = _context?._rawStore?.Keys()?.Length ?? 0,
            UserStoreUsers = _context?._rawUserStore?.Users()?.Length ?? 0,
            RegisteredWebhooks = _context?._rawWebhooks?.List() ?? Array.Empty<string>(),
            RpcMethods = _context?._rawRpc?.Methods() ?? Array.Empty<string>(),
            LoadedAt = LoadedAt
        };

        public Task<bool> HandleRequestAsync(HttpContext context)
        {
            if (_disposed || !_loaded) return Task.FromResult(false);
            return _context._rawRoutes.TryHandleAsync(context);
        }

        public bool DispatchWebhook(string name, string body,
            System.Collections.Generic.IDictionary<string, string> headers)
        {
            if (_disposed || !_loaded) return false;
            return _context._rawWebhooks?.Dispatch(name, body, headers) ?? false;
        }

        public Task FireEventAsync(string eventName, object data)
        {
            if (_disposed || !_loaded) return Task.CompletedTask;
            return _context._rawJellyfin.FireEvent(eventName, data);
        }

        public void ForceGc()
        {
            if (_disposed) return;
            GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
            GC.WaitForPendingFinalizers();
        }

        private void DisposeEngine()
        {
            try { _engine?.Dispose(); } catch { }
            _engine = null;
            _loaded = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(JsRuntime),
                    $"Runtime for mod {_context?.ModId} has been disposed");
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _loaded = false;
                _logger.LogInformation("[JellyFrame] Disposing runtime for mod {ModId}", _context?.ModId);

                _context?.InvokeStop();

                try { _context?._rawJellyfin?.Off("*"); } catch { }

                DisposeEngine();
                _context?.Dispose();
                _context = null;
            }
        }

        private sealed class JsConsole
        {
            private readonly ILogger _logger;
            private readonly string _modId;
            public JsConsole(ILogger logger, string modId) { _logger = logger; _modId = modId; }
            public void Log(string msg) => _logger.LogInformation("[Mod:{Id}] {Msg}", _modId, msg);
            public void Info(string msg) => _logger.LogInformation("[Mod:{Id}] {Msg}", _modId, msg);
            public void Warn(string msg) => _logger.LogWarning("[Mod:{Id}] {Msg}", _modId, msg);
            public void Error(string msg) => _logger.LogError("[Mod:{Id}] {Msg}", _modId, msg);
            public void Debug(string msg) => _logger.LogDebug("[Mod:{Id}] {Msg}", _modId, msg);
        }
    }
}