using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class JellyFrameMiddleware
    {
        private readonly RequestDelegate _next;

        public JellyFrameMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/JellyFrame/mods"))
            {
                var loader = Plugin.Instance?.ModLoader;
                if (loader != null && await loader.TryHandleRequestAsync(context))
                    return;
            }
            await _next(context);
        }
    }
}
