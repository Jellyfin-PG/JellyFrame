using Jellyfin.Plugin.JellyFrame.Runtime;
using Jellyfin.Plugin.JellyFrame.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyFrame
{
    public class PluginServiceRegistrar : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            if (applicationHost?.ApplicationVersion != null)
            {
                Plugin.ServerVersion = applicationHost.ApplicationVersion;
            }

            serviceCollection.AddHostedService<FileTransformationRegistrar>();
            serviceCollection.AddTransient<JellyFrameMiddleware>();
        }
    }
}
