using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    /// <summary>
    /// Shared cross-mod key-value store. All mods that declare the "shared-store"
    /// permission read and write the same backing file, enabling mod cooperation
    /// without needing the event bus.
    ///
    /// Keys are namespaced automatically: a key "foo" set by mod "my-mod" is stored
    /// as "my-mod:foo" in the backing store. This prevents accidental collisions
    /// while still allowing a mod to read another mod's keys by passing the full
    /// namespaced key.
    ///
    /// JS surface:
    ///   jf.kv.set(key, value)         — write (namespaced to this mod)
    ///   jf.kv.get(key)                — read own key
    ///   jf.kv.get("other-mod:key")    — read another mod's key (full namespace)
    ///   jf.kv.delete(key)             — delete own key
    ///   jf.kv.keys()                  — list own keys (without namespace prefix)
    ///   jf.kv.allKeys()               — list all keys in the shared store (with prefixes)
    /// </summary>
    public sealed class KvSurface : IDisposable
    {
        private static Dictionary<string, string> _shared = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _sharedLock = new();
        private static bool _loaded = false;
        private static string _sharedFilePath = null;
        private static bool _dirty = false;
        private static Timer _sharedFlushTimer = null;

        private readonly string _modId;
        private bool _disposed;

        private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(500);

        public KvSurface(string modId, IApplicationPaths paths)
        {
            _modId = modId;
            lock (_sharedLock)
            {
                if (_sharedFilePath == null)
                {
                    var dir = Path.Combine(paths.DataPath, "JellyFrame", "store");
                    Directory.CreateDirectory(dir);
                    _sharedFilePath = Path.Combine(dir, "__shared_kv.json");
                }
                if (!_loaded)
                {
                    LoadShared();
                    _loaded = true;
                }
                _sharedFlushTimer ??= new Timer(_ => FlushIfDirty(),
                    null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Set a value under this mod's namespace.</summary>
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_sharedLock)
            {
                _shared[Ns(key)] = value ?? string.Empty;
                ScheduleFlush();
            }
        }

        /// <summary>
        /// Get a value. If key contains ":" it is treated as a fully-qualified
        /// namespaced key (e.g. "other-mod:mykey"); otherwise the current mod's
        /// namespace is prepended automatically.
        /// </summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            lock (_sharedLock)
            {
                var fqKey = key.Contains(':') ? key : Ns(key);
                _shared.TryGetValue(fqKey, out var val);
                return val;
            }
        }

        /// <summary>Delete a key from this mod's namespace.</summary>
        public void Delete(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_sharedLock)
            {
                if (_shared.Remove(Ns(key)))
                    ScheduleFlush();
            }
        }

        /// <summary>List keys belonging to this mod (without namespace prefix).</summary>
        public string[] Keys()
        {
            var prefix = _modId + ":";
            lock (_sharedLock)
            {
                var result = new List<string>();
                foreach (var k in _shared.Keys)
                    if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result.Add(k.Substring(prefix.Length));
                return result.ToArray();
            }
        }

        /// <summary>List all keys across all mods (fully qualified with namespace prefix).</summary>
        public string[] AllKeys()
        {
            lock (_sharedLock)
                return new List<string>(_shared.Keys).ToArray();
        }

        private string Ns(string key) => _modId + ":" + key;

        private static void LoadShared()
        {
            try
            {
                if (_sharedFilePath != null && File.Exists(_sharedFilePath))
                {
                    var json = File.ReadAllText(_sharedFilePath);
                    _shared = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                              ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                _shared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void ScheduleFlush()
        {
            _dirty = true;
            _sharedFlushTimer?.Change(FlushDelay, Timeout.InfiniteTimeSpan);
        }

        private static void FlushIfDirty()
        {
            lock (_sharedLock)
            {
                if (!_dirty || _sharedFilePath == null) return;
                _dirty = false;
                try
                {
                    File.WriteAllText(_sharedFilePath,
                        JsonSerializer.Serialize(_shared,
                            new JsonSerializerOptions { WriteIndented = false }));
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_sharedLock) { FlushIfDirty(); }
        }
    }
}
