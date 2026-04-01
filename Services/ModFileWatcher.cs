using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Services
{
    public sealed class ModFileWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Func<string, Task> _onModChanged;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<string, Timer> _debounce = new();
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

        private bool _disposed;

        public ModFileWatcher(string cacheDir, Func<string, Task> onModChanged, ILogger logger)
        {
            _onModChanged = onModChanged;
            _logger       = logger;

            Directory.CreateDirectory(cacheDir);

            _watcher = new FileSystemWatcher(cacheDir, "*__serverjs.js")
            {
                NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents   = true
            };

            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            var modId = ParseModId(e.Name);
            if (modId == null) return;

            _debounce.AddOrUpdate(
                modId,
                _ => CreateDebounceTimer(modId),
                (_, existing) =>
                {
                    existing.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
                    return existing;
                });
        }

        private Timer CreateDebounceTimer(string modId)
        {
            return new Timer(_ => FireReload(modId), null,
                DebounceDelay, Timeout.InfiniteTimeSpan);
        }

        private void FireReload(string modId)
        {
            if (_disposed) return;
            _logger.LogInformation(
                "[JellyFrame] Hot-reload triggered for mod '{Id}' — cache file changed", modId);
            _ = _onModChanged(modId).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception,
                        "[JellyFrame] Hot-reload failed for mod '{Id}'", modId);
            }, TaskScheduler.Default);
        }

        private static string ParseModId(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (!fileName.EndsWith("__serverjs.js", StringComparison.OrdinalIgnoreCase))
                return null;

            var parts = Path.GetFileNameWithoutExtension(fileName)
                .Split(new[] { "__" }, StringSplitOptions.None);

            return parts.Length >= 3 ? parts[0] : null;
        }

        public void Start()  => _watcher.EnableRaisingEvents = true;
        public void Stop()   => _watcher.EnableRaisingEvents = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            foreach (var t in _debounce.Values)
                t.Dispose();
            _debounce.Clear();
        }
    }
}
