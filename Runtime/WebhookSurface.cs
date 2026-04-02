using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class WebhookSurface : IDisposable
    {

        private readonly ConcurrentDictionary<string, WebhookHandler> _handlers = new();

        private readonly string _modId;
        private Jint.Engine _engine;
        private readonly ILogger _logger;

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        private bool _disposed;

        public void SetEngine(Jint.Engine engine) => _engine = engine;

        public WebhookSurface(string modId, ILogger logger)
        {
            _modId = modId;
            _logger = logger;
        }

        public void Register(string name, Jint.Native.JsValue handler)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _handlers[name] = new WebhookHandler(name, handler, _engine);
            _logger.LogInformation("[JellyFrame] Webhook [{Mod}] registered inbound '{Name}'", _modId, name);
        }

        public void Unregister(string name)
        {
            if (_handlers.TryRemove(name, out _))
                _logger.LogInformation("[JellyFrame] Webhook [{Mod}] unregistered '{Name}'", _modId, name);
        }

        public string[] List() => new List<string>(_handlers.Keys).ToArray();

        public WebhookResult Send(string url, object payload, object options = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                return new WebhookResult { Ok = false, Status = 0, Body = "url is required" };

            int timeoutMs = 10_000;
            string secret = null;

            IDictionary<string, object> opts = null;
            if (options is IDictionary<string, object> dOpts)
                opts = dOpts;
            else if (options is Jint.Native.Object.ObjectInstance jsOpts)
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsOpts.GetOwnProperties())
                    d[prop.Key.ToString()] = prop.Value.Value?.ToObject();
                opts = d;
            }

            if (opts != null)
            {
                if (opts.TryGetValue("timeout", out var t) && t != null)
                    timeoutMs = Math.Min(Math.Max(Convert.ToInt32(t), 500), 60_000);
                if (opts.TryGetValue("secret", out var s) && s != null)
                    secret = s.ToString();
            }

            string body = payload is string ps ? ps
                : JsonSerializer.Serialize(payload);

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("User-Agent", "Jellyfin-JellyFrame/1.0");

                if (!string.IsNullOrEmpty(secret))
                {
                    var sig = ComputeHmac(body, secret);
                    request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", "sha256=" + sig);
                }

                var response = _http.SendAsync(request, cts.Token).GetAwaiter().GetResult();
                var respBody = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();

                _logger.LogDebug("[JellyFrame] Webhook [{Mod}] outbound → {Url} {Status}",
                    _modId, url, (int)response.StatusCode);

                return new WebhookResult
                {
                    Ok = response.IsSuccessStatusCode,
                    Status = (int)response.StatusCode,
                    Body = respBody
                };
            }
            catch (OperationCanceledException)
            {
                return new WebhookResult { Ok = false, Status = 408, Body = "Timed out" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] Webhook [{Mod}] outbound to {Url} failed", _modId, url);
                return new WebhookResult { Ok = false, Status = 0, Body = ex.Message };
            }
        }

        public bool Dispatch(string name, string rawBody, IDictionary<string, string> headers)
        {
            if (!_handlers.TryGetValue(name, out var handler)) return false;

            object payload;
            try
            {
                using var doc = JsonDocument.Parse(rawBody ?? "{}");
                payload = ConvertJsonElement(doc.RootElement);
            }
            catch { payload = rawBody; }

            try
            {
                handler.Engine.Invoke(
                    handler.JsHandler,
                    Jint.Native.JsValue.Undefined,
                    new[]
                    {
                        Jint.Native.JsValue.FromObject(handler.Engine, payload),
                        Jint.Native.JsValue.FromObject(handler.Engine, headers)
                    });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[JellyFrame] Webhook [{Mod}] handler '{Name}' threw", _modId, name);
                return false;
            }
        }

        private static object ConvertJsonElement(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in el.EnumerateObject())
                        dict[prop.Name] = ConvertJsonElement(prop.Value);
                    return dict;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in el.EnumerateArray())
                        list.Add(ConvertJsonElement(item));
                    return list;
                case JsonValueKind.String: return el.GetString();
                case JsonValueKind.Number:
                    if (el.TryGetInt64(out var l)) return l;
                    return el.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }

        private static string ComputeHmac(string payload, string secret)
        {
            var key = Encoding.UTF8.GetBytes(secret);
            var data = Encoding.UTF8.GetBytes(payload);
            using var hmac = new System.Security.Cryptography.HMACSHA256(key);
            return BitConverter.ToString(hmac.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handlers.Clear();
        }

        private class WebhookHandler
        {
            public string Name { get; }
            public Jint.Native.JsValue JsHandler { get; }
            public Jint.Engine Engine { get; }

            public WebhookHandler(string name, Jint.Native.JsValue handler, Jint.Engine engine)
            {
                Name = name;
                JsHandler = handler;
                Engine = engine;
            }
        }

        public class WebhookResult
        {
            public bool Ok { get; set; }
            public int Status { get; set; }
            public string Body { get; set; }
        }
    }
}