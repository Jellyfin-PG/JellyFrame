using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class RoutesSurface
    {
        public record RouteHandler(string Method, string Path, JsValue Handler);

        private readonly List<RouteHandler> _routes = new();
        private readonly string             _modId;
        private Engine                      _engine;

        public RoutesSurface(string modId) => _modId = modId;

        public void SetEngine(Engine engine) => _engine = engine;

        public string BasePath => "/JellyFrame/mods/" + _modId + "/api";

        public IReadOnlyList<RouteHandler> Routes => _routes;

        public void Get(string path, JsValue handler)
            => _routes.Add(new RouteHandler("GET", Normalize(path), handler));

        public void Post(string path, JsValue handler)
            => _routes.Add(new RouteHandler("POST", Normalize(path), handler));

        public void Put(string path, JsValue handler)
            => _routes.Add(new RouteHandler("PUT", Normalize(path), handler));

        public void Delete(string path, JsValue handler)
            => _routes.Add(new RouteHandler("DELETE", Normalize(path), handler));

        public void Patch(string path, JsValue handler)
            => _routes.Add(new RouteHandler("PATCH", Normalize(path), handler));

        private string Normalize(string path)
            => "/" + path.TrimStart('/');

        public async Task<bool> TryHandleAsync(HttpContext context)
        {
            if (_engine == null) return false;

            var requestPath = context.Request.Path.Value ?? string.Empty;
            var prefix      = BasePath;

            if (!requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var subPath = requestPath.Substring(prefix.Length);
            if (string.IsNullOrEmpty(subPath)) subPath = "/";

            var method = context.Request.Method.ToUpperInvariant();

            foreach (var route in _routes)
            {
                if (!string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!MatchPath(route.Path, subPath, out var pathParams))
                    continue;

                var req = await BuildRequest(context, pathParams);
                var res = new JsResponse(context);

                var jsReq  = JsValue.FromObject(_engine, req);
                var jsRes  = JsValue.FromObject(_engine, res);
                var result = _engine.Invoke(route.Handler, JsValue.Undefined, new[] { jsReq, jsRes });

                if (result.IsObject())
                {
                    var raw = result.ToObject();
                    if (raw is Task t)
                        await t;
                }

                await res.FlushAsync();

                return true;
            }

            return false;
        }

        private static bool MatchPath(string pattern, string path, out Dictionary<string, string> pathParams)
        {
            pathParams = new Dictionary<string, string>();
            var patternParts = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var pathParts    = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (patternParts.Length != pathParts.Length) return false;

            for (int i = 0; i < patternParts.Length; i++)
            {
                if (patternParts[i].StartsWith(":"))
                    pathParams[patternParts[i].TrimStart(':')] = pathParts[i];
                else if (!string.Equals(patternParts[i], pathParts[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static async Task<JsRequest> BuildRequest(HttpContext context, Dictionary<string, string> pathParams)
        {
            var query = new Dictionary<string, string>();
            foreach (var q in context.Request.Query)
                query[q.Key] = q.Value.ToString();

            var headers = new Dictionary<string, string>();
            foreach (var h in context.Request.Headers)
                headers[h.Key] = h.Value.ToString();

            string body = null;
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                body = await reader.ReadToEndAsync();
            }

            object parsedBody = null;
            if (!string.IsNullOrEmpty(body))
            {
                try { parsedBody = JsonSerializer.Deserialize<object>(body); }
                catch { parsedBody = body; }
            }

            return new JsRequest
            {
                Method     = context.Request.Method,
                Path       = context.Request.Path.Value,
                Query      = query,
                Headers    = headers,
                PathParams = pathParams,
                RawBody    = body,
                Body       = parsedBody
            };
        }
    }

    public class JsRequest
    {
        public string                      Method     { get; set; }
        public string                      Path       { get; set; }
        public Dictionary<string, string>  Query      { get; set; }
        public Dictionary<string, string>  Headers    { get; set; }
        public Dictionary<string, string>  PathParams { get; set; }
        public string                      RawBody    { get; set; }
        public object                      Body       { get; set; }
    }

    public class JsResponse
    {
        private readonly HttpContext _context;
        private Task                 _pendingWrite;

        public bool Sent { get; private set; }

        public JsResponse(HttpContext context) => _context = context;

        public JsResponse Status(int code)
        {
            _context.Response.StatusCode = code;
            return this;
        }

        public Task Json(object data)
        {
            if (Sent) return Task.CompletedTask;
            Sent = true;
            _context.Response.ContentType = "application/json";
            _pendingWrite = _context.Response.WriteAsync(JsonSerializer.Serialize(data));
            return _pendingWrite;
        }

        public Task Html(string html)
        {
            if (Sent) return Task.CompletedTask;
            Sent = true;
            _context.Response.ContentType = "text/html; charset=utf-8";
            _pendingWrite = _context.Response.WriteAsync(html ?? string.Empty);
            return _pendingWrite;
        }

        public Task Send(string text, string contentType = "text/plain")
        {
            if (Sent) return Task.CompletedTask;
            Sent = true;
            _context.Response.ContentType = contentType;
            _pendingWrite = _context.Response.WriteAsync(text);
            return _pendingWrite;
        }

        public Task SendStatus(int code)
        {
            _context.Response.StatusCode = code;
            Sent = true;
            return Task.CompletedTask;
        }

        public void SetHeader(string key, string value)
            => _context.Response.Headers[key] = value;

        public Task FlushAsync()
            => _pendingWrite ?? Task.CompletedTask;
    }
}
