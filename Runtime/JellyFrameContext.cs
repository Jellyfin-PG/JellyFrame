using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class JellyFrameContext : IDisposable
    {
        private Action _startHandler;
        private Action _stopHandler;

        internal readonly StoreSurface _rawStore;
        internal readonly UserStoreSurface _rawUserStore;
        internal readonly KvSurface _rawKv;
        internal readonly SchedulerSurface _rawScheduler;
        internal readonly EventBusSurface _rawBus;
        internal readonly WebhookSurface _rawWebhooks;
        internal readonly RpcSurface _rawRpc;
        internal readonly FileSystemSurface _rawFilesystem;
        internal readonly OsSurface _rawOs;
        internal readonly DbSurface _rawDb;
        internal readonly DbSurface _rawSharedDb;
        internal readonly JellyfinSurface _rawJellyfin;
        internal readonly RoutesSurface _rawRoutes;
        internal readonly HttpSurface _rawHttp;

        public JellyFrameContext(
            string modId,
            Dictionary<string, string> vars,
            JellyfinSurface jellyfin,
            RoutesSurface routes,
            StoreSurface store,
            UserStoreSurface userStore,
            KvSurface kv,
            CacheSurface cache,
            HttpSurface http,
            LogSurface log,
            SchedulerSurface scheduler,
            EventBusSurface bus,
            WebhookSurface webhooks,
            RpcSurface rpc,
            FileSystemSurface filesystem,
            OsSurface os,
            DbSurface db,
            DbSurface sharedDb,
            PermissionSurface permissions)
        {
            ModId = modId;
            Vars = vars;
            Cache = cache;
            cache.PersistentStore = store; // enables jf.cache.set(key, val, ttl, true)
            Log = log;
            Perms = permissions;

            _rawHttp = http;
            _rawUserStore = userStore;
            _rawKv = kv;
            _rawScheduler = scheduler;
            _rawBus = bus;
            _rawWebhooks = webhooks;
            _rawRpc = rpc;
            _rawFilesystem = filesystem;
            _rawOs = os;
            _rawDb = db;
            _rawSharedDb = sharedDb;
            _rawJellyfin = jellyfin;
            _rawRoutes = routes;

            Routes = routes;
            Jellyfin = new GatedJellyfinSurface(jellyfin, permissions);
            Http = new GatedHttpSurface(http, permissions);
            Store = new GatedStoreSurface(store, permissions);
            UserStore = new GatedUserStoreSurface(userStore, permissions);
            Kv = new GatedKvSurface(kv, permissions);
            Scheduler = new GatedSchedulerSurface(scheduler, permissions);
            Bus = new GatedEventBusSurface(bus, permissions);
            Webhooks = new GatedWebhookSurface(webhooks, permissions);
            Rpc = new GatedRpcSurface(rpc, permissions);
            Fs = new GatedFileSystemSurface(filesystem, permissions);
            Os = new GatedOsSurface(os, permissions);
            Db = new GatedDbSurface(db, permissions);
            SharedDb = new GatedSharedDbSurface(sharedDb, permissions);
        }

        public string ModId { get; }
        public Dictionary<string, string> Vars { get; }
        public RoutesSurface Routes { get; }
        public CacheSurface Cache { get; }
        public LogSurface Log { get; }
        public PermissionSurface Perms { get; }

        public GatedJellyfinSurface Jellyfin { get; }
        public GatedHttpSurface Http { get; }
        public GatedStoreSurface Store { get; }
        public GatedUserStoreSurface UserStore { get; }
        public GatedKvSurface Kv { get; }
        public GatedSchedulerSurface Scheduler { get; }
        public GatedEventBusSurface Bus { get; }
        public GatedWebhookSurface Webhooks { get; }
        public GatedRpcSurface Rpc { get; }
        public GatedFileSystemSurface Fs { get; }
        public GatedOsSurface Os { get; }
        public GatedDbSurface Db { get; }
        public GatedSharedDbSurface SharedDb { get; }

        public void OnStart(Action handler) => _startHandler = handler;
        public void OnStop(Action handler) => _stopHandler = handler;

        public void InvokeStart()
        {
            try { _startHandler?.Invoke(); }
            catch (Exception ex) { Log.Error("onStart error: " + ex.Message); }
        }

        public void InvokeStop()
        {
            try { _stopHandler?.Invoke(); }
            catch (Jint.Runtime.StatementsCountOverflowException) { /* expected during teardown */ }
            catch (Jint.Runtime.MemoryLimitExceededException)
            {
                Log.Debug("onStop skipped: engine memory cap reached during teardown (engine disposing anyway)");
            }
            catch (TimeoutException) { /* expected: onStop hit engine timeout during teardown */ }
            catch (Exception ex) { Log.Error("onStop error: " + ex.Message); }
        }

        public void Dispose()
        {
            _rawStore?.Dispose();
            _rawUserStore?.Dispose();
            _rawKv?.Dispose();
            _rawScheduler?.Dispose();
            _rawBus?.Dispose();
            _rawWebhooks?.Dispose();
            _rawRpc?.Dispose();
            _rawDb?.Dispose();
            _rawSharedDb?.Dispose();
        }
    }
}