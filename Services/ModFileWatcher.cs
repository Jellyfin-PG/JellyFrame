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
        private readonly FileSystemWatcher _watcherJs;
        private readonly FileSystemWatcher _watcherCss;
        private readonly Func<string, Task> _onModChanged;
        private readonly Func<string, Task> _onCssChanged;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<string, Timer> _debounce = new();
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

        private bool _disposed;

        public ModFileWatcher(
            string cacheDir,
            Func<string, Task> onModChanged,
            ILogger logger,
            Func<string, Task> onCssChanged = null)
        {
            _onModChanged = onModChanged;
            _onCssChanged = onCssChanged;
            _logger = logger;

            Directory.CreateDirectory(cacheDir);

            _watcherJs = new FileSystemWatcher(cacheDir, "*__serverjs.js")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcherJs.Created += OnJsFileEvent;
            _watcherJs.Changed += OnJsFileEvent;

            _watcherCss = new FileSystemWatcher(cacheDir, "*.css")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = onCssChanged != null
            };
            _watcherCss.Created += OnCssFileEvent;
            _watcherCss.Changed += OnCssFileEvent;
        }

        private void OnJsFileEvent(object sender, FileSystemEventArgs e)
        {
            var modId = ParseModId(e.Name, "serverjs");
            if (modId == null) return;
            Debounce("js:" + modId, () => FireJsReload(modId));
        }

        private void OnCssFileEvent(object sender, FileSystemEventArgs e)
        {
            if (_onCssChanged == null) return;
            var modId = ParseModId(e.Name, "css");
            if (modId == null) return;
            Debounce("css:" + modId, () => FireCssReload(modId));
        }

        private void Debounce(string key, Action action)
        {
            _debounce.AddOrUpdate(
                key,
                _ => CreateDebounceTimer(action),
                (_, existing) =>
                {
                    existing.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
                    return existing;
                });
        }

        private Timer CreateDebounceTimer(Action action)
            => new Timer(_ => action(), null, DebounceDelay, Timeout.InfiniteTimeSpan);

        private void FireJsReload(string modId)
        {
            if (_disposed) return;
            _logger.LogInformation(
                "[JellyFrame] Hot-reload triggered for mod '{Id}' — server JS cache changed", modId);
            _ = _onModChanged(modId).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception,
                        "[JellyFrame] Hot-reload failed for mod '{Id}'", modId);
            }, TaskScheduler.Default);
        }

        private void FireCssReload(string modId)
        {
            if (_disposed || _onCssChanged == null) return;
            _logger.LogInformation(
                "[JellyFrame] CSS hot-reload triggered for mod '{Id}' — CSS cache changed", modId);
            _ = _onCssChanged(modId).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception,
                        "[JellyFrame] CSS hot-reload failed for mod '{Id}'", modId);
            }, TaskScheduler.Default);
        }

        private static string ParseModId(string fileName, string type)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var name = Path.GetFileNameWithoutExtension(fileName);
            var parts = name.Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length < 3) return null;
            if (!string.Equals(parts[2], type, StringComparison.OrdinalIgnoreCase)) return null;
            return parts[0];
        }

        public void Start()
        {
            _watcherJs.EnableRaisingEvents = true;
            _watcherCss.EnableRaisingEvents = _onCssChanged != null;
        }

        public void Stop()
        {
            _watcherJs.EnableRaisingEvents = false;
            _watcherCss.EnableRaisingEvents = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcherJs.EnableRaisingEvents = false;
            _watcherCss.EnableRaisingEvents = false;
            _watcherJs.Dispose();
            _watcherCss.Dispose();
            foreach (var t in _debounce.Values)
                t.Dispose();
            _debounce.Clear();
        }
    }
}
