using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class CacheSurface
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _store = new();

        private const int SweepThreshold = 500;
        private int _setCount;

        public void Set(string key, object value, double ttlMs = 0)
        {
            _store[key] = new CacheEntry
            {
                Value   = value,
                Expires = ttlMs > 0 ? DateTime.UtcNow.AddMilliseconds(ttlMs) : DateTime.MaxValue
            };

            if (++_setCount % SweepThreshold == 0)
                Sweep();
        }

        public object Get(string key)
        {
            if (!_store.TryGetValue(key, out var entry)) return null;
            if (entry.Expires > DateTime.UtcNow) return entry.Value;
            _store.TryRemove(key, out _);
            return null;
        }

        public bool Has(string key)
        {
            if (!_store.TryGetValue(key, out var entry)) return false;
            if (entry.Expires > DateTime.UtcNow) return true;
            _store.TryRemove(key, out _);
            return false;
        }

        public void Delete(string key) => _store.TryRemove(key, out _);

        public void Clear()
        {
            _store.Clear();
            _setCount = 0;
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

        private void Sweep()
        {
            var now  = DateTime.UtcNow;
            var dead = new List<string>();
            foreach (var kv in _store)
                if (kv.Value.Expires <= now)
                    dead.Add(kv.Key);
            foreach (var k in dead)
                _store.TryRemove(k, out _);
        }

        private class CacheEntry
        {
            public object   Value   { get; set; }
            public DateTime Expires { get; set; }
        }
    }
}
