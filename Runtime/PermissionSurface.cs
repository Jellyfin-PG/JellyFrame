using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class PermissionSurface
    {

        public const string Http = "http";
        public const string JellyfinRead = "jellyfin.read";
        public const string JellyfinWrite = "jellyfin.write";
        public const string JellyfinDelete = "jellyfin.delete";
        public const string JellyfinTasks = "jellyfin.tasks";
        public const string JellyfinRefresh = "jellyfin.refresh";
        public const string JellyfinLiveTv = "jellyfin.livetv";
        public const string JellyfinAdmin = "jellyfin.admin";
        public const string Store = "store";
        public const string SharedStore = "shared-store";
        public const string Scheduler = "scheduler";
        public const string Webhooks = "webhooks";
        public const string Rpc = "rpc";
        public const string Bus = "bus";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Http, JellyfinRead, JellyfinWrite, JellyfinDelete, JellyfinTasks,
            JellyfinRefresh, JellyfinLiveTv, JellyfinAdmin,
            Store, SharedStore, Scheduler, Webhooks, Rpc, Bus
        };

        private readonly string _modId;
        private readonly HashSet<string> _granted;

        public PermissionSurface(string modId, IEnumerable<string> declared)
        {
            _modId = modId;
            _granted = new HashSet<string>(
                declared ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (_granted.Contains(JellyfinWrite))
                _granted.Add(JellyfinRead);
        }

        public bool Has(string permission)
            => _granted.Contains(permission);

        private static string PermissionToSurface(string permission)
        {
            switch (permission)
            {
                case Http: return "jf.http";
                case JellyfinRead: return "jf.jellyfin (read methods)";
                case JellyfinWrite: return "jf.jellyfin (write methods)";
                case JellyfinDelete: return "jf.jellyfin (delete methods)";
                case JellyfinTasks: return "jf.jellyfin (scheduled task execution)";
                case JellyfinRefresh: return "jf.jellyfin (metadata refresh)";
                case JellyfinLiveTv:  return "jf.jellyfin (live TV recording management)";
                case JellyfinAdmin:   return "jf.jellyfin (admin: users, sessions, server management)";
                case Store: return "jf.store / jf.userStore";
                case SharedStore: return "jf.kv (shared cross-mod key-value store)";
                case Scheduler: return "jf.scheduler";
                case Webhooks: return "jf.webhooks";
                case Rpc: return "jf.rpc";
                case Bus: return "jf.bus";
                default: return "jf." + permission;
            }
        }

        public void Require(string permission)
        {
            if (!Has(permission))
                throw new UnauthorizedAccessException(
                    $"[JellyFrame] Mod '{_modId}' tried to use {PermissionToSurface(permission)} " +
                    $"but '{permission}' was not declared in the mod's \"permissions\" array.");
        }

        public string[] Granted() => _granted.ToArray();

        public static IEnumerable<string> UnknownPermissions(IEnumerable<string> declared)
            => (declared ?? Enumerable.Empty<string>())
               .Where(p => !All.Contains(p, StringComparer.OrdinalIgnoreCase));
    }
}