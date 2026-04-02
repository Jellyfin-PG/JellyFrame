using System;
using System.Collections.Concurrent;
using System.Threading;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class RpcSurface : IDisposable
    {

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RpcHandler>>
            _registry = new(StringComparer.OrdinalIgnoreCase);

        private readonly string _modId;
        private readonly ILogger _logger;
        private Engine _engine;
        private bool _disposed;

        public RpcSurface(string modId, ILogger logger)
        {
            _modId = modId;
            _logger = logger;
        }

        public void SetEngine(Engine engine) => _engine = engine;

        public void Handle(string method, JsValue handler)
        {
            if (string.IsNullOrWhiteSpace(method) || _engine == null) return;

            var modHandlers = _registry.GetOrAdd(_modId,
                _ => new ConcurrentDictionary<string, RpcHandler>(StringComparer.OrdinalIgnoreCase));

            modHandlers[method] = new RpcHandler(_modId, method, handler, _engine);
            _logger.LogDebug("[JellyFrame] RPC [{Mod}] registered handler '{Method}'", _modId, method);
        }

        public void Unhandle(string method)
        {
            if (_registry.TryGetValue(_modId, out var handlers))
                handlers.TryRemove(method, out _);
        }

        public RpcResult Call(string targetModId, string method,
            object payload = null, double timeoutMs = 5000)
        {
            if (!_registry.TryGetValue(targetModId, out var handlers) ||
                !handlers.TryGetValue(method, out var handler))
            {
                return RpcResult.Fail(
                    $"No handler '{method}' registered on mod '{targetModId}'");
            }

            var timeout = TimeSpan.FromMilliseconds(Math.Max(timeoutMs, 100));
            object result = null;
            Exception error = null;

            using var cts = new CancellationTokenSource(timeout);
            using var done = new ManualResetEventSlim(false);

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var jsPayload = JsValue.FromObject(handler.Engine, payload);
                    var jsResult = handler.Engine.Invoke(
                        handler.JsHandler, JsValue.Undefined, new[] { jsPayload });
                    var resultStr = jsResult?.ToString();
                    result = (resultStr == null || resultStr == "null" || resultStr == "undefined")
                        ? null
                        : jsResult.ToObject();
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });

            bool completed = done.Wait(timeout);

            if (!completed)
                return RpcResult.Fail(
                    $"Timeout calling '{method}' on '{targetModId}' ({(int)timeoutMs}ms)");

            if (error != null)
            {
                _logger.LogError(error,
                    "[JellyFrame] RPC [{From}→{To}] '{Method}' threw",
                    _modId, targetModId, method);
                return RpcResult.Fail(error.Message);
            }

            _logger.LogDebug("[JellyFrame] RPC [{From}→{To}] '{Method}' OK",
                _modId, targetModId, method);
            return RpcResult.Success(result);
        }

        public string[] Methods()
        {
            if (!_registry.TryGetValue(_modId, out var handlers))
                return Array.Empty<string>();
            return new System.Collections.Generic.List<string>(handlers.Keys).ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _registry.TryRemove(_modId, out _);
        }

        private class RpcHandler
        {
            public string ModId { get; }
            public string Method { get; }
            public JsValue JsHandler { get; }
            public Engine Engine { get; }

            public RpcHandler(string modId, string method, JsValue handler, Engine engine)
            {
                ModId = modId;
                Method = method;
                JsHandler = handler;
                Engine = engine;
            }
        }

        public class RpcResult
        {
            public bool Ok { get; private set; }
            public object Value { get; private set; }
            public string Error { get; private set; }

            public static RpcResult Success(object value)
                => new RpcResult { Ok = true, Value = value };
            public static RpcResult Fail(string error)
                => new RpcResult { Ok = false, Error = error };
        }
    }
}