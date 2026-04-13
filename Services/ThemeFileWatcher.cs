using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Services
{
    /// <summary>
    /// Watches the themes CSS cache directory for file changes and invalidates
    /// the affected theme's cache so the next browser request to
    /// ThemeCompiledCssController re-fetches and recompiles the CSS.
    ///
    /// Fully segmented from <see cref="ModFileWatcher"/> — operates only on
    /// JellyFrame/themes/ and calls <see cref="ThemeResourceCache.InvalidateTheme"/>
    /// directly, keeping all invalidation logic self-contained.
    /// </summary>
    public sealed class ThemeFileWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly IApplicationPaths _paths;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<string, Timer> _debounce = new();
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

        private bool _disposed;

        public ThemeFileWatcher(
            string themeCacheDir,
            IApplicationPaths paths,
            ILogger logger)
        {
            _paths = paths;
            _logger = logger;

            Directory.CreateDirectory(themeCacheDir);

            _watcher = new FileSystemWatcher(themeCacheDir, "*.css")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            var themeId = ParseThemeId(e.Name);
            if (themeId == null) return;

            _debounce.AddOrUpdate(
                themeId,
                _ => CreateDebounceTimer(themeId),
                (_, existing) =>
                {
                    existing.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
                    return existing;
                });
        }

        private Timer CreateDebounceTimer(string themeId)
            => new Timer(_ => FireReload(themeId), null,
                DebounceDelay, Timeout.InfiniteTimeSpan);

        private void FireReload(string themeId)
        {
            if (_disposed) return;
            _logger.LogInformation(
                "[JellyFrame:Theme] CSS hot-reload: invalidating cache for theme '{Id}'", themeId);
            ThemeResourceCache.InvalidateTheme(themeId, _paths);
        }

        /// <summary>
        /// Parse the theme ID from a cache filename.
        /// Base cache:  themeId__version__type[__hash].css
        /// Addon cache: themeId--addonId__version__type[__hash].css
        /// The theme ID is the part before the first "--" or "__".
        /// </summary>
        private static string ParseThemeId(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            var name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(name)) return null;

            var doubleDashIdx = name.IndexOf("--", StringComparison.Ordinal);
            var doubleUnderIdx = name.IndexOf("__", StringComparison.Ordinal);

            int cutAt;
            if (doubleDashIdx >= 0 && (doubleUnderIdx < 0 || doubleDashIdx < doubleUnderIdx))
                cutAt = doubleDashIdx;
            else if (doubleUnderIdx >= 0)
                cutAt = doubleUnderIdx;
            else
                return null;

            var themeId = name.Substring(0, cutAt);
            return string.IsNullOrWhiteSpace(themeId) ? null : themeId;
        }

        public void Start() => _watcher.EnableRaisingEvents = true;
        public void Stop() => _watcher.EnableRaisingEvents = false;

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
