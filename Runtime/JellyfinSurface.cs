using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jint.Native.Object;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class JellyfinSurface
    {
        private readonly ILibraryManager _library;
        private readonly IUserDataManager _userData;
        private readonly IUserManager _users;
        private readonly ISessionManager _sessions;
        private readonly ISubtitleManager _subtitles;
        private readonly IMediaEncoder _encoder;
        private readonly IPlaylistManager _playlists;
        private readonly IDtoService _dto;
        private readonly ILogger _logger;

        private readonly List<(string Event, Func<object, Task> Handler)> _eventHandlers = new();
        private readonly object _handlerLock = new();

        public JellyfinSurface(
            ILibraryManager library,
            IUserDataManager userData,
            IUserManager users,
            ISessionManager sessions,
            ISubtitleManager subtitles,
            IMediaEncoder encoder,
            IPlaylistManager playlists,
            IDtoService dto,
            ILogger logger)
        {
            _library = library;
            _userData = userData;
            _users = users;
            _sessions = sessions;
            _subtitles = subtitles;
            _encoder = encoder;
            _playlists = playlists;
            _dto = dto;
            _logger = logger;
        }

        public object GetItem(string id, string userId = null)
        {
            if (!Guid.TryParse(id, out var guid)) return null;
            var item = _library.GetItemById(guid);
            if (item == null) return null;
            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                user = _users.GetUserById(ug);
            return SerializeItem(item, user);
        }

        public object[] GetItems(object query)
        {
            var q = new InternalItemsQuery();

            Dictionary<string, string> d = null;

            if (query is IDictionary<string, object> dict)
            {
                d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in dict)
                    d[kv.Key] = kv.Value?.ToString();
            }
            else if (query is Jint.Native.Object.ObjectInstance jsObj)
            {
                d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsObj.GetOwnProperties())
                {
                    var strVal = prop.Value.Value?.ToString();
                    if (strVal != null && strVal != "null" && strVal != "undefined")
                        d[prop.Key.ToString()] = strVal;
                }
            }

            if (d != null)
            {
                _logger.LogDebug("[JellyFrame] GetItems query keys: {Keys}", string.Join(", ", d.Keys.Select(k => k + "=" + d[k])));

                if (d.TryGetValue("parentId", out var pid) && Guid.TryParse(pid, out var pg))
                    q.ParentId = pg;
                if (d.TryGetValue("userId", out var uid) && !string.IsNullOrEmpty(uid) && Guid.TryParse(uid, out var ug))
                {
                    q.User = _users.GetUserById(ug);
                    _logger.LogDebug("[JellyFrame] GetItems userId={Uid} resolved user={User}", uid, q.User?.Username ?? "null");
                }
                else if (d.TryGetValue("userId", out var uidRaw))
                {
                    _logger.LogDebug("[JellyFrame] GetItems userId present but failed to resolve: '{Raw}'", uidRaw);
                }
                if (d.TryGetValue("limit", out var lim) && int.TryParse(lim, out var l))
                    q.Limit = l;
                if (d.TryGetValue("startIndex", out var si) && int.TryParse(si, out var s))
                    q.StartIndex = s;
                if (d.TryGetValue("type", out var type) && !string.IsNullOrEmpty(type))
                    q.IncludeItemTypes = new[] { Enum.Parse<BaseItemKind>(type, true) };
                if (d.TryGetValue("recursive", out var rec))
                    q.Recursive = rec == "true";
                if (d.TryGetValue("searchTerm", out var st))
                    q.SearchTerm = st;
                if (d.TryGetValue("sortBy", out var sb) && Enum.TryParse<ItemSortBy>(sb, true, out var isb))
                {
                    var order = d.TryGetValue("sortOrder", out var so) && so?.ToLower() == "descending"
                        ? Jellyfin.Database.Implementations.Enums.SortOrder.Descending
                        : Jellyfin.Database.Implementations.Enums.SortOrder.Ascending;
                    q.OrderBy = new[] { (isb, order) };
                }
                if (d.TryGetValue("isFavorite", out var fav))
                    q.IsFavoriteOrLiked = fav == "true";
                if (d.TryGetValue("mediaTypes", out var mt) && !string.IsNullOrEmpty(mt))
                    q.MediaTypes = new[] { Enum.Parse<MediaType>(mt, true) };
            }
            else
            {
                _logger.LogDebug("[JellyFrame] GetItems query was null or unrecognised type: {Type}", query?.GetType().FullName ?? "null");
            }
            var user = q.User;
            _logger.LogDebug("[JellyFrame] GetItems executing with user={User}", user?.Username ?? "null");
            return _library.GetItemsResult(q).Items.Select(i => SerializeItem(i, user)).ToArray();
        }

        public object GetItemByPath(string path)
        {
            var item = _library.FindByPath(path, false);
            return item == null ? null : SerializeItem(item);
        }

        public object[] Search(string searchTerm, int limit = 20)
            => _library.GetItemsResult(new InternalItemsQuery
            {
                SearchTerm = searchTerm,
                Limit = limit,
                Recursive = true
            }).Items.Select(i => SerializeItem(i)).ToArray();

        public object[] GetLatestItems(string userId, int limit = 20)
        {
            if (!Guid.TryParse(userId, out var guid)) return Array.Empty<object>();
            var user = _users.GetUserById(guid);
            if (user == null) return Array.Empty<object>();
            return _library.GetItemsResult(new InternalItemsQuery
            {
                User = user,
                Limit = limit,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) }
            }).Items.Select(i => SerializeItem(i, user)).ToArray();
        }

        public object[] GetResumeItems(string userId, int limit = 10)
        {
            if (!Guid.TryParse(userId, out var guid)) return Array.Empty<object>();
            var user = _users.GetUserById(guid);
            if (user == null) return Array.Empty<object>();
            return _library.GetItemsResult(new InternalItemsQuery
            {
                User = user,
                Limit = limit,
                Recursive = true,
                IsResumable = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) }
            }).Items.Select(i => SerializeItem(i, user)).ToArray();
        }

        public object[] GetUserLibraries(string userId)
        {
            if (!Guid.TryParse(userId, out var guid)) return Array.Empty<object>();
            var user = _users.GetUserById(guid);
            if (user == null) return Array.Empty<object>();
            return _library.GetUserRootFolder().GetChildren(user, true)
                .Select(i => SerializeItem(i, user)).ToArray();
        }

        public bool SetFavorite(string userId, string itemId, bool isFavorite)
        {
            if (!Guid.TryParse(userId, out var ug) || !Guid.TryParse(itemId, out var ig)) return false;
            var user = _users.GetUserById(ug);
            var item = _library.GetItemById(ig);
            if (user == null || item == null) return false;
            var data = _userData.GetUserData(user, item);
            if (data == null) return false;
            data.IsFavorite = isFavorite;
            _userData.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
            return true;
        }

        public bool SetWatched(string userId, string itemId, bool watched)
        {
            if (!Guid.TryParse(userId, out var ug) || !Guid.TryParse(itemId, out var ig)) return false;
            var user = _users.GetUserById(ug);
            var item = _library.GetItemById(ig);
            if (user == null || item == null) return false;
            var data = _userData.GetUserData(user, item);
            if (data == null) return false;
            data.Played = watched;
            if (watched) data.LastPlayedDate = DateTime.UtcNow;
            _userData.SaveUserData(user, item, data, UserDataSaveReason.TogglePlayed, CancellationToken.None);
            return true;
        }

        public void RefreshLibrary()
            => _library.QueueLibraryScan();

        private static bool GetPermission(User u, PermissionKind kind)
        {
            try
            {
                var perm = u.Permissions?.FirstOrDefault(p => p.Kind == kind);
                return perm != null && perm.Value;
            }
            catch
            {
                return false;
            }
        }

        public object[] GetUsers()
            => _users.Users.Select(u => (object)new
            {
                id = u.Id.ToString("N"),
                name = u.Username,
                isAdmin = GetPermission(u, PermissionKind.IsAdministrator),
                isDisabled = GetPermission(u, PermissionKind.IsDisabled),
                lastLoginDate = u.LastLoginDate,
                lastActivityDate = u.LastActivityDate,
                hasPassword = !string.IsNullOrEmpty(u.Password)
            }).ToArray();

        public object GetUser(string id)
        {
            if (!Guid.TryParse(id, out var guid)) return null;
            var u = _users.GetUserById(guid);
            if (u == null) return null;
            return new
            {
                id = u.Id.ToString("N"),
                name = u.Username,
                isAdmin = GetPermission(u, PermissionKind.IsAdministrator),
                isDisabled = GetPermission(u, PermissionKind.IsDisabled),
                lastLoginDate = u.LastLoginDate,
                lastActivityDate = u.LastActivityDate,
                hasPassword = !string.IsNullOrEmpty(u.Password)
            };
        }

        public object GetUserByName(string name)
        {
            var u = _users.GetUserByName(name);
            if (u == null) return null;
            return GetUser(u.Id.ToString("N"));
        }

        public object[] GetSessions()
            => _sessions.Sessions.Select(SerializeSession).ToArray();

        public object[] GetSessionsForUser(string userId)
        {
            if (!Guid.TryParse(userId, out var guid)) return Array.Empty<object>();
            return _sessions.Sessions
                .Where(s => s.UserId == guid)
                .Select(SerializeSession).ToArray();
        }

        public Task SendMessageToSession(string sessionId, string header, string text, int timeoutMs = 5000)
            => _sessions.SendMessageCommand(null, sessionId,
                new MessageCommand { Header = header, Text = text, TimeoutMs = timeoutMs },
                CancellationToken.None);

        public Task SendMessageToAllSessions(string header, string text, int timeoutMs = 5000)
            => Task.WhenAll(_sessions.Sessions.Select(s =>
                _sessions.SendMessageCommand(null, s.Id,
                    new MessageCommand { Header = header, Text = text, TimeoutMs = timeoutMs },
                    CancellationToken.None)));

        public Task StopPlayback(string sessionId)
            => _sessions.SendPlaystateCommand(null, sessionId,
                new PlaystateRequest { Command = PlaystateCommand.Stop },
                CancellationToken.None);

        public Task PausePlayback(string sessionId)
            => _sessions.SendPlaystateCommand(null, sessionId,
                new PlaystateRequest { Command = PlaystateCommand.Pause },
                CancellationToken.None);

        public Task ResumePlayback(string sessionId)
            => _sessions.SendPlaystateCommand(null, sessionId,
                new PlaystateRequest { Command = PlaystateCommand.Unpause },
                CancellationToken.None);

        public Task SeekPlayback(string sessionId, long positionTicks)
            => _sessions.SendPlaystateCommand(null, sessionId,
                new PlaystateRequest { Command = PlaystateCommand.Seek, SeekPositionTicks = positionTicks },
                CancellationToken.None);

        public Task PlayItem(string sessionId, string itemId)
        {
            if (!Guid.TryParse(itemId, out var ig)) return Task.CompletedTask;
            return _sessions.SendPlayCommand(null, sessionId,
                new PlayRequest { ItemIds = new[] { ig }, PlayCommand = PlayCommand.PlayNow },
                CancellationToken.None);
        }

        public Task SendNotification(string userId, string name, string description, string type = "Normal")
        {
            _logger.LogWarning("[JellyFrame] jf.jellyfin.sendNotification is not available (INotificationManager removed from SDK)");
            return Task.CompletedTask;
        }

        public Task BroadcastNotification(string name, string description, string type = "Normal")
        {
            _logger.LogWarning("[JellyFrame] jf.jellyfin.broadcastNotification is not available (INotificationManager removed from SDK)");
            return Task.CompletedTask;
        }

        public object[] GetPlaylists(string userId)
        {
            if (!Guid.TryParse(userId, out var guid)) return Array.Empty<object>();
            var user = _users.GetUserById(guid);
            if (user == null) return Array.Empty<object>();
            return _library.GetItemsResult(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Playlist },
                User = user,
                Recursive = true
            }).Items.Select(i => SerializeItem(i, user)).ToArray();
        }

        public async Task<string> CreatePlaylist(string userId, string name, string[] itemIds)
        {
            if (!Guid.TryParse(userId, out var guid)) return null;
            var guids = itemIds?.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
                               .Where(g => g != Guid.Empty).ToArray() ?? Array.Empty<Guid>();
            var result = await _playlists.CreatePlaylist(new MediaBrowser.Model.Playlists.PlaylistCreationRequest
            {
                Name = name,
                UserId = guid,
                ItemIdList = guids
            });
            return result?.Id;
        }

        public Task AddToPlaylist(string playlistId, string[] itemIds, string userId)
        {
            return Task.CompletedTask;
        }

        public object[] GetSubtitleProviders()
            => _subtitles.GetSupportedProviders(null).Select(p => (object)new
            {
                name = p.Name,
                id = p.Id,
                supportsSearch = false
            }).ToArray();

        public Task DownloadSubtitles(string itemId, int subtitleIndex)
        {
            if (!Guid.TryParse(itemId, out var guid)) return Task.CompletedTask;
            var item = _library.GetItemById(guid) as Video;
            if (item == null) return Task.CompletedTask;
            return _subtitles.DownloadSubtitles(item, subtitleIndex.ToString(), CancellationToken.None);
        }

        public string GetEncoderVersion()
            => _encoder.EncoderVersion?.ToString() ?? "unknown";

        public object GetEncoderInfo()
            => new
            {
                version = _encoder.EncoderVersion?.ToString(),
                path = _encoder.EncoderPath,
                isAvailable = !string.IsNullOrEmpty(_encoder.EncoderPath)
            };

        public void On(string eventName, Func<object, Task> handler)
        {
            lock (_handlerLock)
                _eventHandlers.Add((eventName.ToLowerInvariant(), handler));
        }

        public void Off(string eventName)
        {
            lock (_handlerLock)
            {
                if (eventName == "*")
                    _eventHandlers.Clear();
                else
                    _eventHandlers.RemoveAll(e => e.Event == eventName.ToLowerInvariant());
            }
        }

        public async Task FireEvent(string eventName, object data)
        {
            List<(string, Func<object, Task>)> handlers;
            lock (_handlerLock)
                handlers = _eventHandlers
                    .Where(e => e.Event == eventName.ToLowerInvariant())
                    .ToList();

            foreach (var (_, handler) in handlers)
            {
                try
                {
                    var task = handler(data);
                    if (task != null) await task;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyFrame] Event handler error for {Event}", eventName);
                }
            }
        }

        private object SerializeItem(BaseItem item, User user = null)
        {
            // In Jellyfin 10.11, ItemImageInfo has no Tag property.
            // The image tag (e.g. "dc191ddea7315e019f31d75443a1f964") is the MD5 of
            // DateModified.Ticks as a string — exactly how Jellyfin's own GetImageCacheTag
            // computes it in BaseItem.GetVersionInfo and DtoService.AttachBasicFields.
            static string GetTag(ItemImageInfo info)
            {
                if (info == null) return null;
                var ticksBytes = System.Text.Encoding.UTF8.GetBytes(
                    info.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var hash = System.Security.Cryptography.MD5.HashData(ticksBytes);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }

            bool isFavorite = false;
            if (user != null)
            {
                try
                {
                    var udm = _userData ?? BaseItem.UserDataManager;
                    if (udm == null)
                    {
                        _logger.LogWarning("[JellyFrame] SerializeItem: IUserDataManager is null for item {Id}", item.Id);
                    }
                    else
                    {
                        var keys = item.GetUserDataKeys();
                        _logger.LogDebug("[JellyFrame] SerializeItem item={Id} user={User} udataKeys={Keys}",
                            item.Id, user.Username, string.Join(",", keys));
                        var data = udm.GetUserData(user, item);
                        _logger.LogDebug("[JellyFrame] SerializeItem userData={Data} isFavorite={Fav}",
                            data == null ? "null" : "found", data?.IsFavorite);
                        isFavorite = data?.IsFavorite ?? false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JellyFrame] GetUserData failed for item {Id} user {UserId}", item.Id, user.Id);
                }
            }
            else
            {
                _logger.LogDebug("[JellyFrame] SerializeItem item={Id} — no user, isFavorite will be false", item.Id);
            }

            return new
            {
                id = item.Id.ToString("N"),
                name = item.Name,
                type = item.GetBaseItemKind().ToString(),
                path = item.Path,
                overview = item.Overview,
                premiereDate = item.PremiereDate,
                officialRating = item.OfficialRating,
                communityRating = item.CommunityRating,
                genres = item.Genres,
                tags = item.Tags,
                providerIds = item.ProviderIds,
                parentId = item.ParentId.ToString("N"),
                productionYear = item.ProductionYear,
                runTimeTicks = item.RunTimeTicks,
                isFavorite = isFavorite,
                dateCreated = item.DateCreated,
                dateModified = item.DateModified,
                seriesName = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeriesName,
                seasonName = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeasonName,
                indexNumber = item.IndexNumber,
                imageTags = item.ImageInfos == null ? null : new
                {
                    Primary = item.ImageInfos.FirstOrDefault(i => i.Type == ImageType.Primary) is var p && p != null ? GetTag(p) : null,
                    Thumb = item.ImageInfos.FirstOrDefault(i => i.Type == ImageType.Thumb) is var th && th != null ? GetTag(th) : null,
                    Banner = item.ImageInfos.FirstOrDefault(i => i.Type == ImageType.Banner) is var bn && bn != null ? GetTag(bn) : null,
                    Logo = item.ImageInfos.FirstOrDefault(i => i.Type == ImageType.Logo) is var l && l != null ? GetTag(l) : null,
                    Backdrop = item.ImageInfos.FirstOrDefault(i => i.Type == ImageType.Backdrop) is var bd && bd != null ? GetTag(bd) : null
                },
                backdropImageTags = item.ImageInfos == null ? Array.Empty<string>()
                    : item.ImageInfos
                        .Where(i => i.Type == ImageType.Backdrop)
                        .Select(i => GetTag(i))
                        .ToArray()
            };
        }

        private static object SerializeSession(SessionInfo s) => new
        {
            id = s.Id,
            userId = s.UserId.ToString("N"),
            userName = s.UserName,
            client = s.Client,
            deviceName = s.DeviceName,
            deviceId = s.DeviceId,
            remoteEndPoint = s.RemoteEndPoint,
            lastActivityDate = s.LastActivityDate,
            supportsMediaControl = s.SupportsMediaControl,
            nowPlayingItem = s.NowPlayingItem == null ? null : new
            {
                id = s.NowPlayingItem.Id,
                name = s.NowPlayingItem.Name,
                type = s.NowPlayingItem.Type
            },
            playState = s.PlayState == null ? null : new
            {
                positionTicks = s.PlayState.PositionTicks,
                isPaused = s.PlayState.IsPaused,
                isMuted = s.PlayState.IsMuted,
                volumeLevel = s.PlayState.VolumeLevel,
                playMethod = (string)null
            }
        };
    }
}