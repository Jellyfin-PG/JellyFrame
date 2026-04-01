using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
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

        public object GetItem(string id)
        {
            if (!Guid.TryParse(id, out var guid)) return null;
            var item = _library.GetItemById(guid);
            return item == null ? null : SerializeItem(item);
        }

        public object[] GetItems(object query)
        {
            var q = new InternalItemsQuery();
            if (query is IDictionary<string, object> d)
            {
                if (d.TryGetValue("parentId", out var pid) && Guid.TryParse(pid?.ToString(), out var pg))
                    q.ParentId = pg;
                if (d.TryGetValue("userId", out var uid) && Guid.TryParse(uid?.ToString(), out var ug))
                    q.User = _users.GetUserById(ug);
                if (d.TryGetValue("limit", out var lim) && int.TryParse(lim?.ToString(), out var l))
                    q.Limit = l;
                if (d.TryGetValue("startIndex", out var si) && int.TryParse(si?.ToString(), out var s))
                    q.StartIndex = s;
                if (d.TryGetValue("type", out var type) && !string.IsNullOrEmpty(type?.ToString()))
                    q.IncludeItemTypes = new[] { Enum.Parse<BaseItemKind>(type.ToString(), true) };
                if (d.TryGetValue("recursive", out var rec))
                    q.Recursive = rec?.ToString() == "true";
                if (d.TryGetValue("searchTerm", out var st))
                    q.SearchTerm = st?.ToString();
                if (d.TryGetValue("sortBy", out var sb) && Enum.TryParse<ItemSortBy>(sb?.ToString(), true, out var isb))
                    q.OrderBy = new[] { (isb, Jellyfin.Database.Implementations.Enums.SortOrder.Ascending) };
                else if (d.TryGetValue("sortBy", out _))
                    q.OrderBy = new[] { (ItemSortBy.SortName, Jellyfin.Database.Implementations.Enums.SortOrder.Ascending) };
                if (d.TryGetValue("isFavorite", out var fav))
                    q.IsFavoriteOrLiked = fav?.ToString() == "true";
                if (d.TryGetValue("mediaTypes", out var mt) && mt != null)
                    q.MediaTypes = new[] { Enum.Parse<MediaType>(mt.ToString(), true) };
            }
            return _library.GetItemsResult(q).Items.Select(SerializeItem).ToArray();
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
            }).Items.Select(SerializeItem).ToArray();

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
            }).Items.Select(SerializeItem).ToArray();
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
            }).Items.Select(SerializeItem).ToArray();
        }

        public object[] GetUserLibraries(string userId)
        {
            if (!Guid.TryParse(userId, out var guid)) return Array.Empty<object>();
            var user = _users.GetUserById(guid);
            if (user == null) return Array.Empty<object>();
            return _library.GetUserRootFolder().GetChildren(user, true)
                .Select(SerializeItem).ToArray();
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

        public object[] GetUsers()
            => _users.Users.Select(u => (object)new
            {
                id = u.Id.ToString("N"),
                name = u.Username,
                isAdmin = false,
                isDisabled = false,
                lastLoginDate = u.LastLoginDate,
                lastActivityDate = u.LastActivityDate,
                hasPassword = false
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
                isAdmin = false,
                isDisabled = false,
                lastLoginDate = u.LastLoginDate,
                lastActivityDate = u.LastActivityDate,
                hasPassword = false
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
            _logger.LogWarning("[JellyFrame] mm.jellyfin.sendNotification is not available (INotificationManager removed from SDK)");
            return Task.CompletedTask;
        }

        public Task BroadcastNotification(string name, string description, string type = "Normal")
        {
            _logger.LogWarning("[JellyFrame] mm.jellyfin.broadcastNotification is not available (INotificationManager removed from SDK)");
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
            }).Items.Select(SerializeItem).ToArray();
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
                try { await handler(data); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyFrame] Event handler error for {Event}", eventName);
                }
            }
        }

        private static object SerializeItem(BaseItem item) => new
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
            isFavorite = false,
            dateCreated = item.DateCreated,
            dateModified = item.DateModified
        };

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
