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
        private const int MaxTimeoutMs     = 120_000;

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

            IDictionary<string, object> opts = null;
            if (options is IDictionary<string, object> d) opts = d;

            if (opts != null &&
                opts.TryGetValue("headers", out var rawHdrs) &&
                rawHdrs is IDictionary<string, object> hdrs)
            {
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
                    Status  = (int)response.StatusCode,
                    Ok      = response.IsSuccessStatusCode,
                    Body    = responseBody,
                    Headers = responseHeaders
                };
            }
            catch (OperationCanceledException)
            {
                return new HttpResult
                {
                    Status  = 408,
                    Ok      = false,
                    Body    = "Request timed out",
                    Headers = new Dictionary<string, string>()
                };
            }
            catch (Exception ex)
            {
                return new HttpResult
                {
                    Status  = 0,
                    Ok      = false,
                    Body    = ex.Message,
                    Headers = new Dictionary<string, string>()
                };
            }
        }

        public class HttpResult
        {
            public int                        Status  { get; set; }
            public bool                       Ok      { get; set; }
            public string                     Body    { get; set; }
            public Dictionary<string, string> Headers { get; set; }

            public object Json()
            {
                if (string.IsNullOrWhiteSpace(Body)) return null;
                try
                {
                    return JsonSerializer.Deserialize<object>(Body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { return null; }
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
