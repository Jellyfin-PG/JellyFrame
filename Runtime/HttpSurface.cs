using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class HttpSurface
    {

        private static readonly HttpClient _client = new HttpClient
        {

            Timeout = Timeout.InfiniteTimeSpan
        };

        private const int DefaultTimeoutMs = 30_000;
        private const int MaxTimeoutMs = 120_000;

        public HttpResult Get(string url, object options = null)
            => Run(BuildRequest(HttpMethod.Get, url, body: null, options));

        public HttpResult Post(string url, string body = null, object options = null)
            => Run(BuildRequest(HttpMethod.Post, url, body, options));

        public HttpResult Put(string url, string body = null, object options = null)
            => Run(BuildRequest(HttpMethod.Put, url, body, options));

        public HttpResult Delete(string url, object options = null)
            => Run(BuildRequest(HttpMethod.Delete, url, body: null, options));

        public HttpResult Patch(string url, string body = null, object options = null)
            => Run(BuildRequest(HttpMethod.Patch, url, body, options));

        private static HttpRequestMessage BuildRequest(
            HttpMethod method, string url, string body, object options)
        {
            var request = new HttpRequestMessage(method, url);

            // Options may arrive as IDictionary (from C#) or ObjectInstance (from Jint JS)
            IDictionary<string, object> opts = null;
            if (options is IDictionary<string, object> dOpts)
            {
                opts = dOpts;
            }
            else if (options is Jint.Native.Object.ObjectInstance jsOpts)
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsOpts.GetOwnProperties())
                {
                    var v = prop.Value.Value?.ToString();
                    if (v != null && v != "null" && v != "undefined")
                        d[prop.Key.ToString()] = v;
                    else
                        d[prop.Key.ToString()] = prop.Value.Value?.ToObject();
                }
                opts = d;
            }

            if (opts != null &&
                opts.TryGetValue("headers", out var rawHdrs))
            {
                IDictionary<string, object> hdrs = null;
                if (rawHdrs is IDictionary<string, object> dHdrs)
                    hdrs = dHdrs;
                else if (rawHdrs is Jint.Native.Object.ObjectInstance jsHdrs)
                {
                    var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in jsHdrs.GetOwnProperties())
                        d[prop.Key.ToString()] = prop.Value.Value?.ToString();
                    hdrs = d;
                }
                if (hdrs != null)
                    foreach (var kv in hdrs)
                        request.Headers.TryAddWithoutValidation(kv.Key, kv.Value?.ToString());
            }

            if (body != null)
            {
                string ct = "application/json";
                if (opts != null && opts.TryGetValue("contentType", out var rawCt))
                    ct = rawCt?.ToString() ?? ct;

                request.Content = new StringContent(body, Encoding.UTF8, ct);
            }

            return request;
        }

        private static HttpResult Run(HttpRequestMessage request)
        {

            int timeoutMs = DefaultTimeoutMs;

            using var cts = new CancellationTokenSource(
                Math.Min(Math.Max(timeoutMs, 1000), MaxTimeoutMs));

            try
            {
                var response = _client.SendAsync(request, cts.Token)
                    .GetAwaiter().GetResult();

                var responseBody = response.Content
                    .ReadAsStringAsync(cts.Token)
                    .GetAwaiter().GetResult();

                var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in response.Headers)
                    responseHeaders[h.Key] = string.Join(", ", h.Value);
                foreach (var h in response.Content.Headers)
                    responseHeaders[h.Key] = string.Join(", ", h.Value);

                return new HttpResult
                {
                    Status = (int)response.StatusCode,
                    Ok = response.IsSuccessStatusCode,
                    Body = responseBody,
                    Headers = responseHeaders
                };
            }
            catch (OperationCanceledException)
            {
                return new HttpResult
                {
                    Status = 408,
                    Ok = false,
                    Body = "Request timed out",
                    Headers = new Dictionary<string, string>()
                };
            }
            catch (Exception ex)
            {
                return new HttpResult
                {
                    Status = 0,
                    Ok = false,
                    Body = ex.Message,
                    Headers = new Dictionary<string, string>()
                };
            }
        }

        public class HttpResult
        {
            public int Status { get; set; }
            public bool Ok { get; set; }
            public string Body { get; set; }
            public Dictionary<string, string> Headers { get; set; }

            public object Json()
            {
                if (string.IsNullOrWhiteSpace(Body)) return null;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(Body);
                    return ConvertJsonElement(doc.RootElement);
                }
                catch { return null; }
            }

            private static object ConvertJsonElement(System.Text.Json.JsonElement el)
            {
                switch (el.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Object:
                        var expando = new System.Dynamic.ExpandoObject()
                            as IDictionary<string, object>;
                        foreach (var prop in el.EnumerateObject())
                            expando[prop.Name] = ConvertJsonElement(prop.Value);
                        return expando;
                    case System.Text.Json.JsonValueKind.Array:
                        var list = new List<object>();
                        foreach (var item in el.EnumerateArray())
                            list.Add(ConvertJsonElement(item));
                        return list;
                    case System.Text.Json.JsonValueKind.String:
                        return el.GetString();
                    case System.Text.Json.JsonValueKind.Number:
                        if (el.TryGetInt64(out var l)) return l;
                        return el.GetDouble();
                    case System.Text.Json.JsonValueKind.True: return true;
                    case System.Text.Json.JsonValueKind.False: return false;
                    default: return null;
                }
            }

            public string Header(string name)
            {
                if (Headers == null) return null;
                Headers.TryGetValue(name, out var val);
                return val;
            }
        }
    }
}