using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Api;

namespace Jellyfin.Plugin.JellyFrame.Controllers
{
    [ApiController]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Route("JellyFrame/api/health")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHealth()
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable);

            var loader = plugin.ModLoader;
            var config = plugin.Configuration;

            var allMods   = new List<Services.ModEntry>();
            var enabledIds = config.EnabledMods ?? new List<string>();

            if (!string.IsNullOrWhiteSpace(config.CachedMods))
            {
                try
                {
                    allMods = JsonSerializer.Deserialize<List<Services.ModEntry>>(
                        config.CachedMods,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                        ?? allMods;
                }
                catch { }
            }

            var modIndex = new Dictionary<string, Services.ModEntry>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var m in allMods) modIndex[m.Id] = m;

            var mods = loader.GetHealthSnapshots();

            return Ok(new
            {
                plugin = new
                {
                    version         = plugin.Version?.ToString(),
                    totalCachedMods = allMods.Count,
                    enabledMods     = enabledIds.Count,
                    loadedServerMods = loader.LoadedCount,
                    watcherActive   = loader.WatcherActive
                },
                mods = mods.Select(h => {
                    modIndex.TryGetValue(h.ModId, out var me);
                    return new
                    {
                        id               = h.ModId,
                        name             = me?.Name ?? h.ModId,
                        loaded           = h.IsLoaded,
                        routeCount       = h.RouteCount,
                        schedulerTasks   = h.SchedulerTasks,
                        cacheEntries     = h.CacheEntries,
                        storeKeys        = h.StoreKeys,
                        userStoreUsers   = h.UserStoreUsers,
                        registeredWebhooks = h.RegisteredWebhooks,
                        rpcMethods       = h.RpcMethods,
                        loadedAt         = h.LoadedAt,
                        permissions      = me?.Permissions ?? new List<string>(),
                        requires         = me?.Requires    ?? new List<string>(),
                        // DB
                        dbTables         = h.DbTables,
                        sharedDbTables   = h.SharedDbTables,
                        // Crash / restart
                        crashCount       = h.CrashCount,
                        lastCrashAt      = h.LastCrashAt,
                        lastError        = h.LastError,
                        restartOnCrash   = h.RestartOnCrash,
                        nextRestartAt    = h.NextRestartAt,
                        // Bus / KV
                        busSubscriptions = h.BusSubscriptions,
                        kvKeys           = h.KvKeys
                    };
                })
            });
        }
    }
}
