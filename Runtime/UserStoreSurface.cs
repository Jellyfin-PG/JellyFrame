using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class UserStoreSurface : IDisposable
    {
        private readonly string _modDir;
        private readonly ConcurrentDictionary<string, UserBucket> _buckets = new();
        private bool _disposed;

        private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(500);

        public UserStoreSurface(string modId, IApplicationPaths paths)
        {
            _modDir = Path.Combine(paths.DataPath, "JellyFrame", "userstore", Sanitize(modId));
            Directory.CreateDirectory(_modDir);
        }

        public string Get(string userId, string key)
            => Bucket(userId).Get(key);

        public void Set(string userId, string key, string value)
            => Bucket(userId).Set(key, value);

        public void Delete(string userId, string key)
            => Bucket(userId).Delete(key);

        public void Clear(string userId)
            => Bucket(userId).Clear();

        public string[] Keys(string userId)
            => Bucket(userId).Keys();

        public string[] Users()
        {
            var ids = new List<string>();

            foreach (var kv in _buckets)
                if (kv.Value.HasData) ids.Add(kv.Key);

            if (Directory.Exists(_modDir))
                foreach (var file in Directory.GetFiles(_modDir, "*.json"))
                {
                    var uid = Path.GetFileNameWithoutExtension(file);
                    if (!_buckets.ContainsKey(uid)) ids.Add(uid);
                }
            return ids.ToArray();
        }

        private UserBucket Bucket(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("userId must not be empty");
            return _buckets.GetOrAdd(userId, uid =>
                new UserBucket(Path.Combine(_modDir, Sanitize(uid) + ".json"), FlushDelay));
        }

        private static string Sanitize(string s)
        {

            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "unknown")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var b in _buckets.Values)
                b.Dispose();
        }

        private class UserBucket : IDisposable
        {
            private readonly string _filePath;
            private Dictionary<string, string> _data = new();
            private readonly object _lock = new();
            private bool _dirty;
            private readonly Timer _timer;
            private bool _disposed;

            public bool HasData { get { lock (_lock) return _data.Count > 0; } }

            public UserBucket(string filePath, TimeSpan flushDelay)
            {
                _filePath = filePath;
                _timer = new Timer(_ => FlushIfDirty(), null,
                    Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                Load();
            }

            public string Get(string key)
            {
                lock (_lock) { _data.TryGetValue(key, out var v); return v; }
            }

            public void Set(string key, string value)
            {
                lock (_lock) { _data[key] = value ?? string.Empty; Schedule(); }
            }

            public void Delete(string key)
            {
                lock (_lock) { if (_data.Remove(key)) Schedule(); }
            }

            public void Clear()
            {
                lock (_lock) { _data.Clear(); Schedule(); }
            }

            public string[] Keys()
            {
                lock (_lock) return new List<string>(_data.Keys).ToArray();
            }

            private void Load()
            {
                try
                {
                    if (File.Exists(_filePath))
                        _data = JsonSerializer.Deserialize<Dictionary<string, string>>(
                            File.ReadAllText(_filePath)) ?? new();
                }
                catch { _data = new(); }
            }

            private void Schedule()
            {
                _dirty = true;
                _timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
            }

            private void FlushIfDirty()
            {
                lock (_lock)
                {
                    if (!_dirty) return;
                    _dirty = false;
                    try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_data)); }
                    catch { }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _timer.Dispose();
                FlushIfDirty();
            }
        }
    }
}
