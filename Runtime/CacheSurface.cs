using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class CacheSurface
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _store = new();
        // Optional persistent backing — wired up by JellyFrameContext after construction.
        internal StoreSurface PersistentStore { get; set; }

        private const string PersistPrefix = "__cache__";
        private const int SweepThreshold = 500;
        private int _setCount;

        /// <summary>
        /// Store a value in the in-process cache.
        /// <paramref name="ttlMs"/> &gt; 0 expires the entry after that many milliseconds.
        /// <paramref name="persist"/> = true also writes the value to the mod's persistent
        /// store so it survives a server restart (loaded lazily on first Get/Has miss).
        /// </summary>
        public void Set(string key, object value, double ttlMs = 0, bool persist = false)
        {
            var entry = new CacheEntry
            {
                Value   = value,
                Expires = ttlMs > 0 ? DateTime.UtcNow.AddMilliseconds(ttlMs) : DateTime.MaxValue,
                Persist = persist
            };
            _store[key] = entry;

            if (persist && PersistentStore != null)
            {
                try
                {
                    var pe = new PersistedEntry
                    {
                        ValueJson  = System.Text.Json.JsonSerializer.Serialize(value),
                        ExpiresUtc = entry.Expires == DateTime.MaxValue ? (DateTime?)null : entry.Expires
                    };
                    PersistentStore.Set(PersistPrefix + key,
                        System.Text.Json.JsonSerializer.Serialize(pe));
                }
                catch { /* non-fatal — in-memory entry still valid */ }
            }

            if (++_setCount % SweepThreshold == 0)
                Sweep();
        }

        public object Get(string key)
        {
            if (_store.TryGetValue(key, out var entry))
            {
                if (entry.Expires > DateTime.UtcNow) return entry.Value;
                _store.TryRemove(key, out _);
                if (entry.Persist && PersistentStore != null)
                    PersistentStore.Delete(PersistPrefix + key);
                return null;
            }

            // Cache miss — try the persistent store.
            return TryLoadFromStore(key);
        }

        public bool Has(string key)
        {
            if (_store.TryGetValue(key, out var entry))
            {
                if (entry.Expires > DateTime.UtcNow) return true;
                _store.TryRemove(key, out _);
                return false;
            }
            // Check persistent store without fully loading the value.
            if (PersistentStore == null) return false;
            try
            {
                var raw = PersistentStore.Get(PersistPrefix + key);
                if (raw == null) return false;
                var pe = System.Text.Json.JsonSerializer.Deserialize<PersistedEntry>(raw);
                return pe != null && (pe.ExpiresUtc == null || pe.ExpiresUtc > DateTime.UtcNow);
            }
            catch { return false; }
        }

        public void Delete(string key)
        {
            _store.TryRemove(key, out _);
            PersistentStore?.Delete(PersistPrefix + key);
        }

        public void Clear()
        {
            _store.Clear();
            _setCount = 0;
            if (PersistentStore != null)
            {
                try
                {
                    foreach (var k in PersistentStore.Keys())
                        if (k.StartsWith(PersistPrefix, StringComparison.Ordinal))
                            PersistentStore.Delete(k);
                }
                catch { }
            }
        }

        public int Count
        {
            get
            {
                int n = 0;
                var now = DateTime.UtcNow;
                foreach (var kv in _store)
                    if (kv.Value.Expires > now) n++;
                return n;
            }
        }

        private object TryLoadFromStore(string key)
        {
            if (PersistentStore == null) return null;
            try
            {
                var raw = PersistentStore.Get(PersistPrefix + key);
                if (raw == null) return null;
                var pe = System.Text.Json.JsonSerializer.Deserialize<PersistedEntry>(raw);
                if (pe == null) return null;
                if (pe.ExpiresUtc != null && pe.ExpiresUtc <= DateTime.UtcNow)
                {
                    PersistentStore.Delete(PersistPrefix + key);
                    return null;
                }
                object val = null;
                try { val = System.Text.Json.JsonSerializer.Deserialize<object>(pe.ValueJson); } catch { }
                _store[key] = new CacheEntry
                {
                    Value   = val,
                    Expires = pe.ExpiresUtc ?? DateTime.MaxValue,
                    Persist = true
                };
                return val;
            }
            catch { return null; }
        }

        private void Sweep()
        {
            var now  = DateTime.UtcNow;
            var dead = new List<string>();
            foreach (var kv in _store)
                if (kv.Value.Expires <= now)
                    dead.Add(kv.Key);
            foreach (var k in dead)
                if (_store.TryRemove(k, out var e) && e.Persist)
                    PersistentStore?.Delete(PersistPrefix + k);
        }

        private class CacheEntry
        {
            public object   Value   { get; set; }
            public DateTime Expires { get; set; }
            public bool     Persist { get; set; }
        }

        private class PersistedEntry
        {
            public string    ValueJson  { get; set; }
            public DateTime? ExpiresUtc { get; set; }
        }
    }
}
