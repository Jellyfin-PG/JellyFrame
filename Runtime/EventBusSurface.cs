using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class EventBusSurface : IDisposable
    {

        private static readonly ConcurrentDictionary<string, List<BusSubscription>> _subscriptions
            = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object _subLock = new();
        private static int _subIdCounter;

        private readonly string  _modId;
        private readonly ILogger _logger;
        private Engine           _engine;

        private readonly List<string> _ownedIds = new();
        private bool _disposed;

        public EventBusSurface(string modId, ILogger logger)
        {
            _modId  = modId;
            _logger = logger;
        }

        public void SetEngine(Engine engine) => _engine = engine;

        public int Emit(string eventName, object data = null)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return 0;

            List<BusSubscription> subs;
            lock (_subLock)
            {
                if (!_subscriptions.TryGetValue(eventName, out var list) || list.Count == 0)
                    return 0;
                subs = new List<BusSubscription>(list);
            }

            int invoked = 0;
            foreach (var sub in subs)
            {
                if (sub.Engine == null) continue;
                try
                {
                    sub.Engine.Invoke(sub.Handler,
                        JsValue.Undefined,
                        new[]
                        {
                            JsValue.FromObject(sub.Engine, data),
                            JsValue.FromObject(sub.Engine, _modId)
                        });
                    invoked++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[JellyFrame] Bus event '{Event}' handler in mod '{Mod}' threw",
                        eventName, sub.ModId);
                }
            }

            _logger.LogDebug("[JellyFrame] Bus [{From}] emitted '{Event}' → {N} handler(s)",
                _modId, eventName, invoked);
            return invoked;
        }

        public string On(string eventName, JsValue handler)
        {
            if (string.IsNullOrWhiteSpace(eventName) || _engine == null) return null;

            var id = "bus-" + System.Threading.Interlocked.Increment(ref _subIdCounter);
            var sub = new BusSubscription(id, _modId, _engine, handler);

            lock (_subLock)
            {
                if (!_subscriptions.TryGetValue(eventName, out var list))
                {
                    list = new List<BusSubscription>();
                    _subscriptions[eventName] = list;
                }
                list.Add(sub);
                _ownedIds.Add(id);
            }

            _logger.LogDebug("[JellyFrame] Bus [{Mod}] subscribed to '{Event}' (id={Id})",
                _modId, eventName, id);
            return id;
        }

        public void Off(string subscriptionId)
        {
            lock (_subLock)
            {
                foreach (var list in _subscriptions.Values)
                    list.RemoveAll(s => s.Id == subscriptionId);
                _ownedIds.Remove(subscriptionId);
            }
        }

        public void OffAll()
        {
            lock (_subLock)
            {
                var owned = new HashSet<string>(_ownedIds);
                foreach (var list in _subscriptions.Values)
                    list.RemoveAll(s => owned.Contains(s.Id));
                _ownedIds.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OffAll();
        }

        private class BusSubscription
        {
            public string  Id      { get; }
            public string  ModId   { get; }
            public Engine  Engine  { get; }
            public JsValue Handler { get; }

            public BusSubscription(string id, string modId, Engine engine, JsValue handler)
            {
                Id      = id;
                ModId   = modId;
                Engine  = engine;
                Handler = handler;
            }
        }
    }
}
