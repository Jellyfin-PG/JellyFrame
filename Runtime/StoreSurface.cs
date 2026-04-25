using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class StoreSurface : IDisposable
    {
        private readonly string _filePath;
        private Dictionary<string, string> _data = new();
        private readonly object _lock = new();
        private bool _dirty;
        private readonly Timer _flushTimer;
        private bool _disposed;

        private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(500);

        private static readonly JsonSerializerOptions _jsonOpts =
            new JsonSerializerOptions { WriteIndented = false };

        public StoreSurface(string modId, IApplicationPaths paths)
        {
            var dir = Path.Combine(paths.DataPath, "JellyFrame", "store");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, modId + ".json");
            _flushTimer = new Timer(_ => FlushIfDirty(), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Load();
        }

        public string Get(string key)
        {
            lock (_lock)
            {
                _data.TryGetValue(key, out var val);
                return val;
            }
        }

        public void Set(string key, string value)
        {
            lock (_lock)
            {
                _data[key] = value ?? string.Empty;
                ScheduleFlush();
            }
        }

        public void Delete(string key)
        {
            lock (_lock)
            {
                if (_data.Remove(key))
                    ScheduleFlush();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _data.Clear();
                ScheduleFlush();
            }
        }

        public string[] Keys()
        {
            lock (_lock)
            {
                var keys = new string[_data.Count];
                _data.Keys.CopyTo(keys, 0);
                return keys;
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    _data = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                            ?? new Dictionary<string, string>();
                }
            }
            catch { _data = new Dictionary<string, string>(); }
        }

        private void ScheduleFlush()
        {

            _dirty = true;
            _flushTimer.Change(FlushDelay, Timeout.InfiniteTimeSpan);
        }

        private void FlushIfDirty()
        {
            lock (_lock)
            {
                if (!_dirty) return;
                _dirty = false;
                try
                {
                    File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, _jsonOpts));
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer.Dispose();

            FlushIfDirty();
        }
    }
}