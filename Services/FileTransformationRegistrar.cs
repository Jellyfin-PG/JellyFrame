using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Services
{
    public class FileTransformationRegistrar : IHostedService
    {
        private static readonly Guid TransformationId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-456789012345");

        private readonly ILogger<FileTransformationRegistrar> _logger;

        public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            RegisterWithFileTransformation();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private void RegisterWithFileTransformation()
        {
            try
            {
                var ftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);

                if (ftAssembly == null)
                {
                    _logger.LogWarning("[JellyFrame] File Transformation plugin not found.");
                    return;
                }

                var pluginInterfaceType = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                if (pluginInterfaceType == null)
                {
                    _logger.LogWarning("[JellyFrame] Could not find PluginInterface in File Transformation assembly.");
                    return;
                }

                var newtonsoftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json"
                                      && x != typeof(FileTransformationRegistrar).Assembly)
                    ?? AssemblyLoadContext.All
                        .SelectMany(x => x.Assemblies)
                        .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json");

                if (newtonsoftAssembly == null)
                {
                    _logger.LogWarning("[JellyFrame] Could not find Newtonsoft.Json assembly.");
                    return;
                }

                var jobjectType = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JObject");
                var jtokenType  = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JToken");
                var fromObject  = jtokenType.GetMethod("FromObject", new[] { typeof(object) });
                var indexerSet  = jobjectType.GetProperty("Item", new[] { typeof(string) })
                                             ?.GetSetMethod();

                var payload = System.Activator.CreateInstance(jobjectType);

                void Set(string key, object value)
                {
                    var token = fromObject.Invoke(null, new[] { value });
                    indexerSet.Invoke(payload, new[] { key, token });
                }

                Set("id",               TransformationId.ToString());
                Set("fileNamePattern",  "index.html");
                Set("callbackAssembly", typeof(ModInjector).Assembly.FullName);
                Set("callbackClass",    typeof(ModInjector).FullName);
                Set("callbackMethod",   nameof(ModInjector.InjectMods));

                pluginInterfaceType.GetMethod("RegisterTransformation")
                    ?.Invoke(null, new[] { payload });

                _logger.LogInformation("[JellyFrame] Successfully registered mod injection with File Transformation.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] Failed to register with File Transformation.");
            }
        }
    }
}
