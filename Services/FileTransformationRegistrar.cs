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
        private readonly IServiceProvider _serviceProvider;

        public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            RegisterWithFileTransformation();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            DeregisterTransformation();
            return Task.CompletedTask;
        }

        private void RegisterWithFileTransformation()
        {
            try
            {
                bool useLoom = Plugin.Instance?.Configuration?.UseLoomInjector ?? false;
                string fragment = useLoom ? ".Loom" : ".FileTransformation";
                string typeName = useLoom ? "Jellyfin.Plugin.Loom.LoomInterface" : "Jellyfin.Plugin.FileTransformation.PluginInterface";
                string displayName = useLoom ? "Loom" : "File Transformation";

                _logger.LogInformation("[JellyFrame] Probing for {DisplayName} plugin injection...", displayName);

                var targetAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(fragment) ?? false);

                if (targetAssembly == null)
                {
                    _logger.LogWarning("[JellyFrame] {DisplayName} plugin assembly not found.", displayName);
                    return;
                }

                var pluginInterfaceType = targetAssembly.GetType(typeName);
                if (pluginInterfaceType == null)
                {
                    _logger.LogWarning("[JellyFrame] Could not find {TypeName} in {DisplayName} assembly.", typeName, displayName);
                    return;
                }

                if (useLoom)
                {
                    InitializeLoomServiceProvider(targetAssembly);
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

                _logger.LogInformation("[JellyFrame] Successfully registered mod injection with {DisplayName}.", displayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] Failed to register mod injection.");
            }
        }

        private void DeregisterTransformation()
        {
            try
            {
                bool useLoom = Plugin.Instance?.Configuration?.UseLoomInjector ?? false;
                string fragment = useLoom ? ".Loom" : ".FileTransformation";
                string typeName = useLoom ? "Jellyfin.Plugin.Loom.LoomInterface" : "Jellyfin.Plugin.FileTransformation.PluginInterface";
                string displayName = useLoom ? "Loom" : "File Transformation";

                var targetAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(fragment) ?? false);

                if (targetAssembly == null) return;

                var pluginInterfaceType = targetAssembly.GetType(typeName);
                if (pluginInterfaceType == null) return;

                pluginInterfaceType.GetMethod("DeregisterTransformation")
                    ?.Invoke(null, new object[] { TransformationId });

                _logger.LogInformation("[JellyFrame] Successfully deregistered from {DisplayName}.", displayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] Failed to deregister from transformation plugin.");
            }
        }

        public static void UpdateLoomInjection()
        {
            try
            {
                bool useLoom = Plugin.Instance?.Configuration?.UseLoomInjector ?? false;
                if (!useLoom) return;

                var targetAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(".Loom") ?? false);

                if (targetAssembly == null) return;

                var pluginInterfaceType = targetAssembly.GetType("Jellyfin.Plugin.Loom.LoomInterface");
                if (pluginInterfaceType == null) return;

                var newtonsoftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json"
                                      && x != typeof(FileTransformationRegistrar).Assembly)
                    ?? AssemblyLoadContext.All
                        .SelectMany(x => x.Assemblies)
                        .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json");

                if (newtonsoftAssembly == null) return;

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

                pluginInterfaceType.GetMethod("UpdateTransformation")
                    ?.Invoke(null, new[] { payload });
            }
            catch
            {
                // Ignore silently
            }
        }

        private void InitializeLoomServiceProvider(System.Reflection.Assembly loomAssembly)
        {
            try
            {
                Type loomPluginType = loomAssembly.GetType("Jellyfin.Plugin.Loom.Plugin");
                if (loomPluginType == null) return;

                var instanceProp = loomPluginType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                object instance = instanceProp?.GetValue(null);
                if (instance == null) return;

                var serviceProviderProp = loomPluginType.GetProperty("ServiceProvider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (serviceProviderProp != null && serviceProviderProp.GetValue(instance) == null)
                {
                    serviceProviderProp.SetValue(instance, _serviceProvider);
                    _logger.LogDebug("[JellyFrame] Safely initialized Loom plugin ServiceProvider via reflection.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyFrame] Failed to initialize Loom ServiceProvider via reflection.");
            }
        }
    }
}
