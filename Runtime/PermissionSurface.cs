using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class PermissionSurface
    {

        public const string Http           = "http";
        public const string JellyfinRead   = "jellyfin.read";
        public const string JellyfinWrite  = "jellyfin.write";
        public const string Store          = "store";
        public const string Scheduler      = "scheduler";
        public const string Webhooks       = "webhooks";
        public const string Rpc            = "rpc";
        public const string Bus            = "bus";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Http, JellyfinRead, JellyfinWrite, Store, Scheduler, Webhooks, Rpc, Bus
        };

        private readonly string _modId;
        private readonly HashSet<string> _granted;

        public PermissionSurface(string modId, IEnumerable<string> declared)
        {
            _modId   = modId;
            _granted = new HashSet<string>(
                declared ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (_granted.Contains(JellyfinWrite))
                _granted.Add(JellyfinRead);
        }

        public bool Has(string permission)
            => _granted.Contains(permission);

        public void Require(string permission)
        {
            if (!Has(permission))
                throw new UnauthorizedAccessException(
                    $"[JellyFrame] Mod '{_modId}' requires permission '{permission}' " +
                    $"but it was not declared in mods.json. " +
                    $"Add \"{permission}\" to the mod's \"permissions\" array.");
        }

        public string[] Granted() => _granted.ToArray();

        public static IEnumerable<string> UnknownPermissions(IEnumerable<string> declared)
            => (declared ?? Enumerable.Empty<string>())
               .Where(p => !All.Contains(p, StringComparer.OrdinalIgnoreCase));
    }
}
