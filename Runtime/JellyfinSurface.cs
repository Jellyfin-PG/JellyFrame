using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Data.Queries;
using Jint.Native.Object;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
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
        private readonly ICollectionManager _collections;
        private readonly IProviderManager _providers;
        private readonly IActivityManager _activity;
        private readonly ITaskManager _tasks;
        private readonly IDeviceManager _devices;
        private readonly ITVSeriesManager _tvSeries;
        private readonly ILiveTvManager _liveTv;
        private readonly IMediaSourceManager _mediaSources;
        private readonly MediaBrowser.Model.IO.IFileSystem _fileSystem;
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
            ICollectionManager collections,
            IProviderManager providers,
            IActivityManager activity,
            ITaskManager tasks,
            IDeviceManager devices,
            ITVSeriesManager tvSeries,
            ILiveTvManager liveTv,
            IMediaSourceManager mediaSources,
            MediaBrowser.Model.IO.IFileSystem fileSystem,
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
            _collections = collections;
            _providers = providers;
            _activity = activity;
            _tasks = tasks;
            _devices = devices;
            _tvSeries = tvSeries;
            _liveTv = liveTv;
            _mediaSources = mediaSources;
            _fileSystem = fileSystem;
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

        /// <summary>
        /// Batch-fetch multiple items by ID in one call.
        /// Returns only items that were found; silently skips invalid or missing IDs.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetItemsByIds(object ids, string userId = null)
        {
            IEnumerable<string> idList = null;

            if (ids is string s)
            {
                s = s.Trim();
                if (s.StartsWith("["))
                {
                    try { idList = System.Text.Json.JsonSerializer.Deserialize<string[]>(s); }
                    catch { idList = new[] { s }; }
                }
                else
                {
                    idList = s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
                }
            }
            else if (ids is Jint.Native.Array.ArrayInstance jsArr)
            {
                idList = jsArr
                    .Where(v => v.Type != Jint.Runtime.Types.Null && v.Type != Jint.Runtime.Types.Undefined)
                    .Select(v => v.ToString())
                    .Where(v => !string.IsNullOrEmpty(v));
            }
            else if (ids is System.Collections.IEnumerable enumerable)
            {
                idList = enumerable.Cast<object>().Select(o => o?.ToString()).Where(v => !string.IsNullOrEmpty(v));
            }

            if (idList == null) return Array.Empty<object>();

            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                user = _users.GetUserById(ug);

            var results = new List<object>();
            foreach (var id in idList)
            {
                if (!Guid.TryParse(id, out var guid)) continue;
                var item = _library.GetItemById(guid);
                if (item != null) results.Add(SerializeItem(item, user));
            }
            return results.ToArray();
        }

        /// <summary>
        /// Returns all collections/box-sets visible to a user.
        /// If userId is null, returns all collections in the library.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetCollections(string userId = null)
        {
            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                user = _users.GetUserById(ug);

            var q = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                Recursive = true
            };
            if (user != null) q.User = user;

            return _library.GetItemsResult(q).Items
                .Select(i => SerializeItem(i, user))
                .ToArray();
        }

        /// <summary>
        /// Returns all top-level library folders regardless of user.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetLibraries()
        {
            return _library.GetItemsResult(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.CollectionFolder },
                Recursive = false
            }).Items.Select(i => SerializeItem(i)).ToArray();
        }

        /// <summary>
        /// Returns the count of items matching a query without fetching their full data.
        /// Supports the same query fields as getItems().
        /// Requires: jellyfin.read
        /// </summary>
        public int GetItemCount(object query)
        {
            var q = new InternalItemsQuery();
            if (query is IDictionary<string, object> dictObj)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in dictObj)
                    if (kv.Value != null) d[kv.Key] = kv.Value.ToString();
                ApplyQueryDict(q, d);
            }
            else if (query is Jint.Native.Object.ObjectInstance jsObj)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsObj.GetOwnProperties())
                {
                    var v = prop.Value.Value?.ToString();
                    if (v != null && v != "null" && v != "undefined")
                        d[prop.Key.ToString()] = v;
                }
                ApplyQueryDict(q, d);
            }
            q.Limit = null;
            return _library.GetCount(q);
        }

        /// <summary>
        /// Returns all genres present in the library, optionally scoped to a parent folder.
        /// Each entry: { id, name, itemCount }
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetGenres(string parentId = null, string userId = null)
        {
            var q = new InternalItemsQuery { Recursive = true };
            if (!string.IsNullOrEmpty(parentId) && Guid.TryParse(parentId, out var pg))
                q.ParentId = pg;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                q.User = _users.GetUserById(ug);

            return _library.GetGenres(q).Items
                .Select(tuple => (object)new
                {
                    id = tuple.Item1.Id.ToString("N"),
                    name = tuple.Item1.Name,
                    itemCount = tuple.Item2.ItemCount
                }).ToArray();
        }

        /// <summary>
        /// Returns all studios present in the library, optionally scoped to a parent folder.
        /// Each entry: { id, name, itemCount }
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetStudios(string parentId = null, string userId = null)
        {
            var q = new InternalItemsQuery { Recursive = true };
            if (!string.IsNullOrEmpty(parentId) && Guid.TryParse(parentId, out var pg))
                q.ParentId = pg;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                q.User = _users.GetUserById(ug);

            return _library.GetStudios(q).Items
                .Select(tuple => (object)new
                {
                    id = tuple.Item1.Id.ToString("N"),
                    name = tuple.Item1.Name,
                    itemCount = tuple.Item2.ItemCount
                }).ToArray();
        }

        /// <summary>
        /// Returns a person (actor/director/etc) by name.
        /// Returns null if not found.
        /// Requires: jellyfin.read
        /// </summary>
        /// <summary>
        /// Returns the full people list for an item, optionally filtered by role type.
        /// type: "Actor" | "Director" | "Writer" | "Producer" | "GuestStar" | null (all)
        ///
        /// Each entry: { name, role, type, sortOrder, personId, imageTag, overview, birthDate, birthPlace, deathDate }
        /// </summary>
        public object[] GetItemPeople(string itemId, string type = null)
        {
            if (!Guid.TryParse(itemId, out var guid)) return Array.Empty<object>();

            PersonKind pk = default;
            var hasPk = !string.IsNullOrEmpty(type) && Enum.TryParse<PersonKind>(type, true, out pk);

            var allPeople = _library.GetPeople(new InternalPeopleQuery { ItemId = guid });
            var people = hasPk
                ? allPeople.Where(p => p.Type == pk).ToList()
                : allPeople;
            var results = new List<object>(people.Count);

            foreach (var p in people)
            {
                string personId = null, imageTag = null, overview = null, birthPlace = null;
                DateTime? birthDate = null, deathDate = null;
                try
                {
                    var entity = _library.GetPerson(p.Name);
                    if (entity != null)
                    {
                        personId = entity.Id.ToString("N");
                        overview = entity.Overview;
                        birthDate = entity.PremiereDate;
                        deathDate = entity.EndDate;
                        birthPlace = entity.ProductionLocations?.Length > 0
                            ? entity.ProductionLocations[0] : null;
                        var img = entity.ImageInfos?.FirstOrDefault(
                            x => x.Type == MediaBrowser.Model.Entities.ImageType.Primary);
                        if (img != null)
                        {
                            var tb = System.Text.Encoding.UTF8.GetBytes(
                                img.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            imageTag = Convert.ToHexString(
                                System.Security.Cryptography.MD5.HashData(tb)).ToLowerInvariant();
                        }
                    }
                }
                catch { }

                results.Add(new
                {
                    name = p.Name,
                    role = p.Role,
                    type = p.Type.ToString(),
                    sortOrder = p.SortOrder,
                    personId = personId,
                    imageTag = imageTag,
                    overview = overview,
                    birthDate = birthDate,
                    deathDate = deathDate,
                    birthPlace = birthPlace
                });
            }
            return results.ToArray();
        }

        /// <summary>Lightweight person lookup — returns basic entity fields. Use GetPersonDetails for full bio + filmography.</summary>
        public object GetPerson(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var person = _library.GetPerson(name);
            return person == null ? null : SerializeItem(person);
        }

        /// <summary>
        /// Returns full biographical details for a named person.
        /// Also returns their filmography — items in the library they appear in.
        /// </summary>
        public object GetPersonDetails(string name, string userId = null, int filmographyLimit = 50)
        {
            var person = _library.GetPerson(name);
            if (person == null) return null;

            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                user = _users.GetUserById(ug);

            // Filmography — items in the library that credit this person
            object[] filmography = Array.Empty<object>();
            try
            {
                filmography = _library.GetItemsResult(new InternalItemsQuery
                {
                    Person = name,
                    Recursive = true,
                    Limit = filmographyLimit,
                    User = user,
                    OrderBy = new[] { (ItemSortBy.PremiereDate,
                        Jellyfin.Database.Implementations.Enums.SortOrder.Descending) }
                }).Items.Select(i => SerializeItem(i, user)).ToArray();
            }
            catch { }

            string imageTag = null;
            try
            {
                var img = person.ImageInfos?.FirstOrDefault(
                    x => x.Type == MediaBrowser.Model.Entities.ImageType.Primary);
                if (img != null)
                {
                    var tb = System.Text.Encoding.UTF8.GetBytes(
                        img.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    imageTag = Convert.ToHexString(
                        System.Security.Cryptography.MD5.HashData(tb)).ToLowerInvariant();
                }
            }
            catch { }

            return new
            {
                id = person.Id.ToString("N"),
                name = person.Name,
                overview = person.Overview,
                birthDate = person.PremiereDate,
                deathDate = person.EndDate,
                birthPlace = person.ProductionLocations?.Length > 0 ? person.ProductionLocations[0] : null,
                imageTag = imageTag,
                providerIds = person.ProviderIds,
                filmography = filmography
            };
        }
        /// <summary>
        /// Returns items that feature a specific person (by name), optionally filtered by type.
        /// itemType: optional BaseItemKind string e.g. "Movie", "Episode"
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetPersonItems(string personName, string userId = null,
            string itemType = null, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(personName)) return Array.Empty<object>();
            var q = new InternalItemsQuery
            {
                Person = personName,
                Recursive = true,
                Limit = Math.Max(1, Math.Min(limit, 1000))
            };
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                q.User = _users.GetUserById(ug);
            if (!string.IsNullOrEmpty(itemType) && Enum.TryParse<BaseItemKind>(itemType, true, out var kind))
                q.IncludeItemTypes = new[] { kind };

            var user = q.User;
            return _library.GetItemsResult(q).Items
                .Select(i => SerializeItem(i, user)).ToArray();
        }

        /// <summary>
        /// Returns "next up" episodes for a user — the next unwatched episode in each
        /// in-progress series.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetNextUp(string userId, int limit = 20, string seriesId = null)
        {
            if (!Guid.TryParse(userId, out var ug)) return Array.Empty<object>();
            var user = _users.GetUserById(ug);
            if (user == null) return Array.Empty<object>();

            var query = new MediaBrowser.Model.Querying.NextUpQuery
            {
                User = user,
                Limit = Math.Max(1, Math.Min(limit, 200))
            };
            if (!string.IsNullOrEmpty(seriesId) && Guid.TryParse(seriesId, out var sg))
                query.SeriesId = sg;

            return _tvSeries.GetNextUp(query, new DtoOptions(false)).Items
                .Select(i => SerializeItem(i, user)).ToArray();
        }

        /// <summary>
        /// Returns items similar to the given item.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetSimilarItems(string itemId, string userId = null, int limit = 12)
        {
            if (!Guid.TryParse(itemId, out var guid)) return Array.Empty<object>();
            var item = _library.GetItemById(guid);
            if (item == null) return Array.Empty<object>();

            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                user = _users.GetUserById(ug);

            var q = new InternalItemsQuery
            {
                Recursive = true,
                Limit = Math.Max(1, Math.Min(limit, 200)),
                IncludeItemTypes = new[] { item.GetBaseItemKind() },
                ExcludeItemIds = new[] { guid }
            };
            if (user != null) q.User = user;

            if (item.Genres != null && item.Genres.Length > 0)
                q.Genres = item.Genres;

            return _library.GetItemsResult(q).Items
                .Select(i => SerializeItem(i, user)).ToArray();
        }

        /// <summary>
        /// Returns aggregate item counts for a user broken down by media type.
        /// Returns: { movieCount, seriesCount, episodeCount, songCount, albumCount, ... }
        /// Requires: jellyfin.read
        /// </summary>
        public object GetItemCounts(string userId = null)
        {
            var q = new InternalItemsQuery { Recursive = true };
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                q.User = _users.GetUserById(ug);

            var counts = _library.GetItemCounts(q);
            return new
            {
                movieCount = counts.MovieCount,
                seriesCount = counts.SeriesCount,
                episodeCount = counts.EpisodeCount,
                artistCount = counts.ArtistCount,
                albumCount = counts.AlbumCount,
                songCount = counts.SongCount,
                bookCount = counts.BookCount,
                boxSetCount = counts.BoxSetCount,
                musicVideoCount = counts.MusicVideoCount,
                trailerCount = counts.TrailerCount,
                itemCount = counts.ItemCount
            };
        }

        /// <summary>
        /// Returns live TV channels, optionally scoped to a user.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetChannels(string userId = null, int limit = 100, int startIndex = 0)
        {
            var query = new LiveTvChannelQuery
            {
                Limit = Math.Max(1, Math.Min(limit, 1000)),
                StartIndex = startIndex
            };
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                query.UserId = ug;

            var result = _liveTv.GetInternalChannels(query, new DtoOptions(false), CancellationToken.None);
            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug2))
                user = _users.GetUserById(ug2);

            return (result?.Items ?? Array.Empty<BaseItem>())
                .Select(i => SerializeItem(i, user))
                .ToArray();
        }

        /// <summary>
        /// Returns EPG programs matching the given query.
        /// Useful query fields (via InternalItemsQuery): ChannelIds, IsAiring, IsNews, IsMovie,
        /// IsSports, IsKids, IsSeries, MinStartDate, MaxStartDate, MinEndDate, MaxEndDate.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetPrograms(object query, string userId = null)
        {
            var q = new InternalItemsQuery { Recursive = true };
            if (query is IDictionary<string, object> dictObj)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in dictObj)
                    if (kv.Value != null) d[kv.Key] = kv.Value.ToString();
                ApplyQueryDict(q, d);
                ApplyLiveTvQueryDict(q, d);
            }
            else if (query is Jint.Native.Object.ObjectInstance jsObj)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsObj.GetOwnProperties())
                {
                    var v = prop.Value.Value?.ToString();
                    if (v != null && v != "null" && v != "undefined")
                        d[prop.Key.ToString()] = v;
                }
                ApplyQueryDict(q, d);
                ApplyLiveTvQueryDict(q, d);
            }

            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
            {
                user = _users.GetUserById(ug);
                q.User = user;
            }

            q.IncludeItemTypes = new[] { BaseItemKind.LiveTvProgram };

            return _liveTv.GetPrograms(q, new DtoOptions(false), CancellationToken.None)
                .GetAwaiter().GetResult()
                .Items
                .Select(dto => SerializeProgramDto(dto))
                .ToArray();
        }

        /// <summary>
        /// Returns recorded content.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetRecordings(string userId = null, string channelId = null,
            bool? isInProgress = null, int limit = 50, int startIndex = 0)
        {
            var query = new RecordingQuery
            {
                Limit = Math.Max(1, Math.Min(limit, 500)),
                StartIndex = startIndex
            };
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                query.UserId = ug;
            if (!string.IsNullOrEmpty(channelId))
                query.ChannelId = channelId;
            if (isInProgress.HasValue)
                query.IsInProgress = isInProgress.Value;

            return _liveTv.GetRecordingsAsync(query, new DtoOptions(false))
                .GetAwaiter().GetResult()
                .Items
                .Select(dto => SerializeBaseItemDto(dto))
                .ToArray();
        }

        /// <summary>
        /// Returns scheduled recording timers.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetTimers(string channelId = null, string seriesTimerId = null)
        {
            var query = new TimerQuery();
            if (!string.IsNullOrEmpty(channelId))
                query.ChannelId = channelId;
            if (!string.IsNullOrEmpty(seriesTimerId))
                query.SeriesTimerId = seriesTimerId;

            return _liveTv.GetTimers(query, CancellationToken.None)
                .GetAwaiter().GetResult()
                .Items
                .Select(t => SerializeTimer(t))
                .ToArray();
        }

        /// <summary>
        /// Returns all series recording rules.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetSeriesTimers()
        {
            return _liveTv.GetSeriesTimers(new SeriesTimerQuery(), CancellationToken.None)
                .GetAwaiter().GetResult()
                .Items
                .Select(t => SerializeSeriesTimer(t))
                .ToArray();
        }

        /// <summary>
        /// Returns overall live TV status — enabled, services, enabled users.
        /// Requires: jellyfin.read
        /// </summary>
        public object GetLiveTvInfo()
        {
            var info = _liveTv.GetLiveTvInfo(CancellationToken.None);
            return new
            {
                isEnabled = info.IsEnabled,
                services = (info.Services ?? Array.Empty<LiveTvServiceInfo>())
                                   .Select(s => (object)new { name = s.Name, status = s.Status.ToString(), hasUpdate = s.HasUpdateAvailable })
                                   .ToArray(),
                enabledUsers = (info.EnabledUsers ?? Array.Empty<string>())
            };
        }

        /// <summary>
        /// Schedules a one-time recording for a program.
        /// programId: the EPG program ID to record.
        /// Requires: jellyfin.livetv
        /// </summary>
        public bool CreateTimer(string programId, int prePaddingSeconds = 0, int postPaddingSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(programId)) return false;

            var defaults = _liveTv.GetNewTimerDefaults(CancellationToken.None)
                .GetAwaiter().GetResult();
            if (defaults == null) return false;

            var timer = new MediaBrowser.Model.LiveTv.TimerInfoDto
            {
                ProgramId = programId,
                ChannelId = defaults.ChannelId,
                PrePaddingSeconds = prePaddingSeconds,
                PostPaddingSeconds = postPaddingSeconds
            };

            _liveTv.CreateTimer(timer, CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] CreateTimer: scheduled recording for program '{Id}'", programId);
            return true;
        }

        /// <summary>
        /// Creates a series recording rule from a program's series.
        /// programId: an EPG program ID belonging to the series to record.
        /// Requires: jellyfin.livetv
        /// </summary>
        public bool CreateSeriesTimer(string programId, bool recordNewOnly = true,
            bool recordAnyChannel = false, int prePaddingSeconds = 0, int postPaddingSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(programId)) return false;
            var defaults = _liveTv.GetNewTimerDefaults(programId, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (defaults == null) return false;

            defaults.RecordNewOnly = recordNewOnly;
            defaults.RecordAnyChannel = recordAnyChannel;
            defaults.PrePaddingSeconds = prePaddingSeconds;
            defaults.PostPaddingSeconds = postPaddingSeconds;

            _liveTv.CreateSeriesTimer(defaults, CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] CreateSeriesTimer: series rule created for program '{Id}'", programId);
            return true;
        }

        /// <summary>
        /// Cancels a scheduled one-time recording timer by its ID.
        /// Requires: jellyfin.livetv
        /// </summary>
        public bool CancelTimer(string timerId)
        {
            if (string.IsNullOrWhiteSpace(timerId)) return false;
            _liveTv.CancelTimer(timerId).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] CancelTimer: cancelled timer '{Id}'", timerId);
            return true;
        }

        /// <summary>
        /// Cancels a series recording rule by its ID.
        /// Requires: jellyfin.livetv
        /// </summary>
        public bool CancelSeriesTimer(string seriesTimerId)
        {
            if (string.IsNullOrWhiteSpace(seriesTimerId)) return false;
            _liveTv.CancelSeriesTimer(seriesTimerId).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] CancelSeriesTimer: cancelled series timer '{Id}'", seriesTimerId);
            return true;
        }

        private static object SerializeProgramDto(BaseItemDto dto) => new
        {
            id = dto.Id,
            name = dto.Name,
            channelId = dto.ChannelId,
            channelName = dto.ChannelName,
            startDate = dto.StartDate,
            endDate = dto.EndDate,
            overview = dto.Overview,
            isLive = dto.IsLive,
            isSeries = dto.IsSeries,
            isMovie = dto.IsMovie,
            isKids = dto.IsKids,
            isSports = dto.IsSports,
            isNews = dto.IsNews,
            episodeTitle = dto.EpisodeTitle,
            timerId = dto.TimerId,
            seriesTimerId = dto.SeriesTimerId,
            genres = dto.Genres
        };

        private static object SerializeBaseItemDto(BaseItemDto dto) => new
        {
            id = dto.Id,
            name = dto.Name,
            channelId = dto.ChannelId,
            channelName = dto.ChannelName,
            startDate = dto.StartDate,
            endDate = dto.EndDate,
            overview = dto.Overview,
            status = dto.Status
        };

        private static object SerializeTimer(TimerInfoDto t) => new
        {
            id = t.Id,
            name = t.Name,
            channelId = t.ChannelId,
            channelName = t.ChannelName,
            programId = t.ProgramId,
            startDate = t.StartDate,
            endDate = t.EndDate,
            status = t.Status.ToString(),
            seriesTimerId = t.SeriesTimerId,
            prePaddingSeconds = t.PrePaddingSeconds,
            postPaddingSeconds = t.PostPaddingSeconds
        };

        private static object SerializeSeriesTimer(SeriesTimerInfoDto t) => new
        {
            id = t.Id,
            name = t.Name,
            channelId = t.ChannelId,
            channelName = t.ChannelName,
            programId = t.ProgramId,
            startDate = t.StartDate,
            endDate = t.EndDate,
            recordNewOnly = t.RecordNewOnly,
            recordAnyChannel = t.RecordAnyChannel,
            prePaddingSeconds = t.PrePaddingSeconds,
            postPaddingSeconds = t.PostPaddingSeconds
        };

        private static void ApplyLiveTvQueryDict(InternalItemsQuery q, Dictionary<string, string> d)
        {
            if (d.TryGetValue("isAiring", out var airing)) q.IsAiring = airing == "true";
            if (d.TryGetValue("isMovie", out var movie)) q.IsMovie = movie == "true";
            if (d.TryGetValue("isSeries", out var series)) q.IsSeries = series == "true";
            if (d.TryGetValue("isNews", out var news)) q.IsNews = news == "true";
            if (d.TryGetValue("isKids", out var kids)) q.IsKids = kids == "true";
            if (d.TryGetValue("isSports", out var sports)) q.IsSports = sports == "true";
            if (d.TryGetValue("minStartDate", out var minS) &&
                DateTime.TryParse(minS, null, System.Globalization.DateTimeStyles.RoundtripKind, out var minSd))
                q.MinStartDate = minSd;
            if (d.TryGetValue("maxStartDate", out var maxS) &&
                DateTime.TryParse(maxS, null, System.Globalization.DateTimeStyles.RoundtripKind, out var maxSd))
                q.MaxStartDate = maxSd;
            if (d.TryGetValue("minEndDate", out var minE) &&
                DateTime.TryParse(minE, null, System.Globalization.DateTimeStyles.RoundtripKind, out var minEd))
                q.MinEndDate = minEd;
            if (d.TryGetValue("maxEndDate", out var maxE) &&
                DateTime.TryParse(maxE, null, System.Globalization.DateTimeStyles.RoundtripKind, out var maxEd))
                q.MaxEndDate = maxEd;
        }

        /// <summary>
        /// Returns full user data for an item: play state, position, favourite,
        /// rating, play count, last played date.
        /// Requires: jellyfin.read
        /// </summary>
        public object GetUserData(string itemId, string userId)
        {
            if (!Guid.TryParse(itemId, out var iid)) return null;
            if (!Guid.TryParse(userId, out var uid)) return null;
            var item = _library.GetItemById(iid);
            var user = _users.GetUserById(uid);
            if (item == null || user == null) return null;
            var d = _userData.GetUserData(user, item);
            if (d == null) return null;
            return new
            {
                played = d.Played,
                isFavorite = d.IsFavorite,
                playbackPositionTicks = d.PlaybackPositionTicks,
                playCount = d.PlayCount,
                rating = d.Rating,
                likes = d.Likes,
                lastPlayedDate = d.LastPlayedDate
            };
        }

        /// <summary>
        /// Marks an item as played for a user.
        /// Requires: jellyfin.write
        /// </summary>
        public bool MarkPlayed(string itemId, string userId)
        {
            if (!Guid.TryParse(itemId, out var iid)) return false;
            if (!Guid.TryParse(userId, out var uid)) return false;
            var item = _library.GetItemById(iid);
            var user = _users.GetUserById(uid);
            if (item == null || user == null) return false;
            var data = _userData.GetUserData(user, item);
            data.Played = true;
            data.LastPlayedDate = DateTime.UtcNow;
            if (data.PlayCount == 0) data.PlayCount = 1;
            _userData.SaveUserData(user, item, data, UserDataSaveReason.TogglePlayed, CancellationToken.None);
            return true;
        }

        /// <summary>
        /// Marks an item as unplayed for a user.
        /// Requires: jellyfin.write
        /// </summary>
        public bool MarkUnplayed(string itemId, string userId)
        {
            if (!Guid.TryParse(itemId, out var iid)) return false;
            if (!Guid.TryParse(userId, out var uid)) return false;
            var item = _library.GetItemById(iid);
            var user = _users.GetUserById(uid);
            if (item == null || user == null) return false;
            var data = _userData.GetUserData(user, item);
            data.Played = false;
            data.PlayCount = 0;
            data.LastPlayedDate = null;
            _userData.SaveUserData(user, item, data, UserDataSaveReason.TogglePlayed, CancellationToken.None);
            return true;
        }

        /// <summary>
        /// Sets or clears the favourite flag for an item.
        /// Requires: jellyfin.write
        /// </summary>
        public bool SetFavourite(string itemId, string userId, bool isFavourite)
        {
            if (!Guid.TryParse(itemId, out var iid)) return false;
            if (!Guid.TryParse(userId, out var uid)) return false;
            var item = _library.GetItemById(iid);
            var user = _users.GetUserById(uid);
            if (item == null || user == null) return false;
            var data = _userData.GetUserData(user, item);
            data.IsFavorite = isFavourite;
            _userData.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
            return true;
        }

        /// <summary>
        /// Sets the user rating for an item (0.0-10.0), or null to clear.
        /// Requires: jellyfin.write
        /// </summary>
        public bool SetRating(string itemId, string userId, double? rating)
        {
            if (!Guid.TryParse(itemId, out var iid)) return false;
            if (!Guid.TryParse(userId, out var uid)) return false;
            var item = _library.GetItemById(iid);
            var user = _users.GetUserById(uid);
            if (item == null || user == null) return false;
            var data = _userData.GetUserData(user, item);
            data.Rating = rating.HasValue ? Math.Max(0, Math.Min(10, rating.Value)) : (double?)null;
            _userData.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
            return true;
        }

        /// <summary>
        /// Returns media source info for an item: stream URLs, codec details,
        /// subtitle tracks, audio tracks.
        /// Requires: jellyfin.read
        /// </summary>
        public object GetPlaybackInfo(string itemId, string userId)
        {
            if (!Guid.TryParse(itemId, out var iid)) return null;
            var item = _library.GetItemById(iid);
            if (item == null) return null;
            User user = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var uid))
                user = _users.GetUserById(uid);

            var sources = _mediaSources
                .GetPlaybackMediaSources(item, user, true, false, CancellationToken.None)
                .GetAwaiter().GetResult();

            return sources.Select(s => (object)new
            {
                id = s.Id,
                name = s.Name,
                container = s.Container,
                bitrate = s.Bitrate,
                runTimeTicks = s.RunTimeTicks,
                supportsDirectPlay = s.SupportsDirectPlay,
                supportsDirectStream = s.SupportsDirectStream,
                supportsTranscoding = s.SupportsTranscoding,
                transcodingUrl = s.TranscodingUrl,
                mediaStreams = (s.MediaStreams ?? Array.Empty<MediaBrowser.Model.Entities.MediaStream>())
                    .Select(ms => (object)new
                    {
                        index = ms.Index,
                        type = ms.Type.ToString(),
                        codec = ms.Codec,
                        language = ms.Language,
                        displayTitle = ms.DisplayTitle,
                        isDefault = ms.IsDefault,
                        isForced = ms.IsForced,
                        bitrate = ms.BitRate,
                        width = ms.Width,
                        height = ms.Height,
                        channels = ms.Channels,
                        sampleRate = ms.SampleRate
                    }).ToArray()
            }).ToArray();
        }

        /// <summary>
        /// Reports playback start to Jellyfin, updating Now Playing state.
        /// Requires: jellyfin.write
        /// </summary>
        public bool ReportPlaybackStart(string sessionId, string itemId,
            string mediaSourceId = null, int? audioStreamIndex = null, int? subtitleStreamIndex = null)
        {
            if (!Guid.TryParse(itemId, out var iid)) return false;
            var item = _library.GetItemById(iid);
            if (item == null) return false;
            _sessions.OnPlaybackStart(new PlaybackStartInfo
            {
                SessionId = sessionId,
                ItemId = iid,
                Item = _dto.GetBaseItemDto(item, new DtoOptions(false)),
                MediaSourceId = mediaSourceId,
                AudioStreamIndex = audioStreamIndex,
                SubtitleStreamIndex = subtitleStreamIndex,
                PlaySessionId = Guid.NewGuid().ToString("N")
            }).GetAwaiter().GetResult();
            return true;
        }

        /// <summary>
        /// Reports playback progress, updating position and pause state.
        /// Requires: jellyfin.write
        /// </summary>
        public bool ReportPlaybackProgress(string sessionId, string itemId,
            long? positionTicks = null, bool isPaused = false,
            string mediaSourceId = null, string playSessionId = null)
        {
            if (!Guid.TryParse(itemId, out var iid)) return false;
            var item = _library.GetItemById(iid);
            if (item == null) return false;
            _sessions.OnPlaybackProgress(new PlaybackProgressInfo
            {
                SessionId = sessionId,
                ItemId = iid,
                Item = _dto.GetBaseItemDto(item, new DtoOptions(false)),
                PositionTicks = positionTicks,
                IsPaused = isPaused,
                MediaSourceId = mediaSourceId,
                PlaySessionId = playSessionId ?? Guid.NewGuid().ToString("N")
            }).GetAwaiter().GetResult();
            return true;
        }

        /// <summary>
        /// Reports playback stopped, saving resume position.
        /// Requires: jellyfin.write
        /// </summary>
        public bool ReportPlaybackStopped(string sessionId, string itemId,
            long? positionTicks = null, bool failed = false,
            string mediaSourceId = null, string playSessionId = null)
        {
            if (!Guid.TryParse(itemId, out var iid)) return false;
            var item = _library.GetItemById(iid);
            if (item == null) return false;
            _sessions.OnPlaybackStopped(new PlaybackStopInfo
            {
                SessionId = sessionId,
                ItemId = iid,
                Item = _dto.GetBaseItemDto(item, new DtoOptions(false)),
                PositionTicks = positionTicks,
                Failed = failed,
                MediaSourceId = mediaSourceId,
                PlaySessionId = playSessionId ?? Guid.NewGuid().ToString("N")
            }).GetAwaiter().GetResult();
            return true;
        }

        /// <summary>
        /// Replaces all tags on an item.
        /// Requires: jellyfin.write
        /// </summary>
        public bool SetTags(string itemId, object tags)
        {
            if (!Guid.TryParse(itemId, out var guid)) return false;
            var item = _library.GetItemById(guid);
            if (item == null) return false;
            item.Tags = ExtractStringArray(tags);
            _library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None)
                .GetAwaiter().GetResult();
            return true;
        }

        /// <summary>
        /// Sets the official content rating (e.g. "PG-13", "TV-MA") on an item.
        /// Requires: jellyfin.write
        /// </summary>
        public bool SetOfficialRating(string itemId, string rating)
        {
            if (!Guid.TryParse(itemId, out var guid)) return false;
            var item = _library.GetItemById(guid);
            if (item == null) return false;
            item.OfficialRating = rating;
            _library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None)
                .GetAwaiter().GetResult();
            return true;
        }

        /// <summary>
        /// Creates a new box-set / collection. Returns the new collection ID, or null.
        /// Requires: jellyfin.write
        /// </summary>
        public string CreateCollection(string name, object itemIds = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var ids = ExtractStringArray(itemIds)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();
            var opts = new CollectionCreationOptions
            {
                Name = name,
                ItemIdList = ids.Select(g => g.ToString("N")).ToArray()
            };
            var result = _collections.CreateCollectionAsync(opts).GetAwaiter().GetResult();
            return result?.Id.ToString("N");
        }

        /// <summary>
        /// Adds items to an existing collection.
        /// Requires: jellyfin.write
        /// </summary>
        public bool AddToCollection(string collectionId, object itemIds)
        {
            if (!Guid.TryParse(collectionId, out var cid)) return false;
            var ids = ExtractStringArray(itemIds)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).ToList();
            if (ids.Count == 0) return false;
            _collections.AddToCollectionAsync(cid, ids).GetAwaiter().GetResult();
            return true;
        }

        /// <summary>
        /// Creates a new playlist. Returns the new playlist ID, or null.
        /// Requires: jellyfin.write
        /// </summary>
        public string CreatePlaylist(string name, object itemIds = null, string userId = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var ids = ExtractStringArray(itemIds)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).ToList();
            Guid ownerGuid = Guid.Empty;
            if (!string.IsNullOrEmpty(userId)) Guid.TryParse(userId, out ownerGuid);
            var req = new MediaBrowser.Model.Playlists.PlaylistCreationRequest
            {
                Name = name,
                UserId = ownerGuid,
                ItemIdList = ids
            };
            var result = _playlists.CreatePlaylist(req).GetAwaiter().GetResult();
            return result?.Id;
        }

        /// <summary>
        /// Adds items to an existing playlist.
        /// Requires: jellyfin.write
        /// </summary>
        public bool AddToPlaylist(string playlistId, object itemIds, string userId = null)
        {
            if (!Guid.TryParse(playlistId, out var pid)) return false;
            var ids = ExtractStringArray(itemIds)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).ToList();
            if (ids.Count == 0) return false;
            Guid uid = Guid.Empty;
            if (!string.IsNullOrEmpty(userId)) Guid.TryParse(userId, out uid);
            _playlists.AddItemToPlaylistAsync(pid, ids, uid).GetAwaiter().GetResult();
            return true;
        }

        /// <summary>
        /// Queues a full library scan.
        /// Requires: jellyfin.tasks
        /// </summary>
        public void ScanLibrary()
        {
            _library.QueueLibraryScan();
            _logger.LogInformation("[JellyFrame] ScanLibrary: library scan queued");
        }

        /// <summary>
        /// Returns all active sessions with device, user, now-playing, and transcoding info.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetActiveSessions()
        {
            return _sessions.Sessions
                .Where(s => s.IsActive)
                .Select(s =>
                {
                    var ti = s.TranscodingInfo;
                    return (object)new
                    {
                        id = s.Id,
                        userId = s.UserId.ToString("N"),
                        userName = s.UserName,
                        client = s.Client,
                        deviceName = s.DeviceName,
                        deviceId = s.DeviceId,
                        appVersion = s.ApplicationVersion,
                        remoteEndPoint = s.RemoteEndPoint,
                        lastActivityDate = s.LastActivityDate,
                        supportsRemoteControl = s.SupportsRemoteControl,
                        nowPlayingItem = s.NowPlayingItem == null ? null : (object)new
                        {
                            id = s.NowPlayingItem.Id,
                            name = s.NowPlayingItem.Name,
                            type = s.NowPlayingItem.Type
                        },
                        playState = s.PlayState == null ? null : (object)new
                        {
                            positionTicks = s.PlayState.PositionTicks,
                            isPaused = s.PlayState.IsPaused,
                            isMuted = s.PlayState.IsMuted,
                            volumeLevel = s.PlayState.VolumeLevel
                        },
                        transcodingInfo = ti == null ? null : (object)new
                        {
                            videoCodec = ti.VideoCodec,
                            audioCodec = ti.AudioCodec,
                            container = ti.Container,
                            isVideoDirect = ti.IsVideoDirect,
                            isAudioDirect = ti.IsAudioDirect,
                            bitrate = ti.Bitrate,
                            framerate = ti.Framerate,
                            completionPercentage = ti.CompletionPercentage
                        }
                    };
                }).ToArray();
        }

        /// <summary>
        /// Terminates an active session by its ID.
        /// Requires: jellyfin.admin
        /// </summary>
        public bool TerminateSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return false;
            var session = _sessions.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return false;
            _sessions.ReportSessionEnded(sessionId).AsTask().GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] TerminateSession: ended session {Id}", sessionId);
            return true;
        }

        /// <summary>
        /// Creates a new user. Returns the new user ID, or null on failure.
        /// Requires: jellyfin.admin
        /// </summary>
        public string CreateUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            var user = _users.CreateUserAsync(username).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] CreateUser: created user '{Name}' ({Id})", username, user?.Id);
            return user?.Id.ToString("N");
        }

        /// <summary>
        /// Deletes a user by their ID.
        /// Requires: jellyfin.admin
        /// </summary>
        public bool DeleteUser(string userId)
        {
            if (!Guid.TryParse(userId, out var uid)) return false;
            _users.DeleteUserAsync(uid).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] DeleteUser: deleted user {Id}", userId);
            return true;
        }

        /// <summary>
        /// Resets a user password (generates a new reset PIN).
        /// Requires: jellyfin.admin
        /// </summary>
        public bool ResetUserPassword(string userId)
        {
            if (!Guid.TryParse(userId, out var uid)) return false;
            var user = _users.GetUserById(uid);
            if (user == null) return false;
            _users.ResetPassword(user).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] ResetUserPassword: reset password for user {Id}", userId);
            return true;
        }

        private static string[] ExtractStringArray(object input)
        {
            if (input == null) return Array.Empty<string>();
            if (input is string s)
                return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (input is Jint.Native.Array.ArrayInstance jsArr)
                return jsArr
                    .Where(v => v.Type != Jint.Runtime.Types.Null && v.Type != Jint.Runtime.Types.Undefined)
                    .Select(v => v.ToString())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToArray();
            if (input is System.Collections.IEnumerable enumerable)
                return enumerable.Cast<object>()
                    .Where(o => o != null)
                    .Select(o => o.ToString())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToArray();
            return Array.Empty<string>();
        }

        public bool RefreshMetadata(string itemId, bool replaceAll = false)
        {
            if (!Guid.TryParse(itemId, out var guid)) return false;
            var item = _library.GetItemById(guid);
            if (item == null) return false;

            var opts = new MediaBrowser.Controller.Providers.MetadataRefreshOptions(
                new MediaBrowser.Controller.Providers.DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = replaceAll
                    ? MediaBrowser.Controller.Providers.MetadataRefreshMode.FullRefresh
                    : MediaBrowser.Controller.Providers.MetadataRefreshMode.Default,
                ReplaceAllMetadata = replaceAll,
                EnableRemoteContentProbe = true
            };

            _providers.QueueRefresh(guid, opts, MediaBrowser.Controller.Providers.RefreshPriority.Normal);
            _logger.LogInformation("[JellyFrame] RefreshMetadata: queued refresh for item {Id} (replaceAll={Replace})",
                item.Id, replaceAll);
            return true;
        }

        private void ApplyQueryDict(InternalItemsQuery q, Dictionary<string, string> d)
        {
            if (d.TryGetValue("parentId", out var pid) && Guid.TryParse(pid, out var pg))
                q.ParentId = pg;
            if (d.TryGetValue("userId", out var uid) && Guid.TryParse(uid, out var ug))
                q.User = _users.GetUserById(ug);
            if (d.TryGetValue("limit", out var lim) && int.TryParse(lim, out var l))
                q.Limit = l;
            if (d.TryGetValue("startIndex", out var si) && int.TryParse(si, out var s))
                q.StartIndex = s;
            if (d.TryGetValue("type", out var type) && !string.IsNullOrEmpty(type)
                && Enum.TryParse<BaseItemKind>(type, true, out var kind))
                q.IncludeItemTypes = new[] { kind };
            if (d.TryGetValue("recursive", out var rec))
                q.Recursive = rec == "true";
            if (d.TryGetValue("searchTerm", out var st))
                q.SearchTerm = st;
            if (d.TryGetValue("isFavorite", out var fav))
                q.IsFavoriteOrLiked = fav == "true";
        }
        public bool DeleteItem(string itemId)
        {
            if (!Guid.TryParse(itemId, out var guid)) return false;
            var item = _library.GetItemById(guid);
            if (item == null) return false;

            _library.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false });
            _logger.LogInformation("[JellyFrame] DeleteItem: removed item {Id} ({Name}) from library", item.Id, item.Name);
            return true;
        }

        /// <summary>
        /// Downloads an image from a URL and saves it as the specified image type on an item.
        /// imageType: "Primary", "Backdrop", "Banner", "Logo", "Thumb", "Art", "Disc", "Box", "Screenshot", "Menu", "Chapter", "BoxRear", "Profile"
        /// Requires: jellyfin.write
        /// </summary>
        public bool SetImage(string itemId, string imageType, string url)
        {
            if (!Guid.TryParse(itemId, out var guid)) return false;
            var item = _library.GetItemById(guid);
            if (item == null) return false;
            if (!Enum.TryParse<ImageType>(imageType, ignoreCase: true, out var imgType)) return false;
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("JellyFrame/1.0");

                var stream = httpClient.GetStreamAsync(url).GetAwaiter().GetResult();
                var mimeType = url.Contains(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                             : url.Contains(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                             : "image/jpeg";

                _providers.SaveImage(item, stream, mimeType, imgType, null, CancellationToken.None)
                          .GetAwaiter().GetResult();

                _logger.LogInformation("[JellyFrame] SetImage: saved {Type} image for item {Id} from {Url}",
                    imageType, item.Id, url);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] SetImage failed for item {Id}, type {Type}, url {Url}",
                    itemId, imageType, url);
                return false;
            }
        }

        /// <summary>
        /// Update writable metadata fields on a library item.
        /// Supported fields: name, overview, officialRating, communityRating,
        /// productionYear, genres (string[]), tags (string[]),
        /// providerIds (object/dictionary), premiereDate (ISO string).
        /// Returns true on success, false if item not found or fields dict is empty.
        /// </summary>
        public bool UpdateMetadata(string itemId, object fields)
        {
            if (!Guid.TryParse(itemId, out var guid)) return false;
            var item = _library.GetItemById(guid);
            if (item == null) return false;

            Dictionary<string, string> d = null;
            if (fields is IDictionary<string, object> dictObj)
            {
                d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in dictObj)
                    if (kv.Value != null) d[kv.Key] = kv.Value.ToString();
            }
            else if (fields is Jint.Native.Object.ObjectInstance jsObj)
            {
                d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsObj.GetOwnProperties())
                {
                    var v = prop.Value.Value?.ToString();
                    if (v != null && v != "null" && v != "undefined")
                        d[prop.Key.ToString()] = v;
                }
            }

            if (d == null || d.Count == 0) return false;

            bool changed = false;

            if (d.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            { item.Name = name; changed = true; }

            if (d.TryGetValue("overview", out var overview))
            { item.Overview = overview; changed = true; }

            if (d.TryGetValue("officialRating", out var rating))
            { item.OfficialRating = rating; changed = true; }

            if (d.TryGetValue("communityRating", out var commRating)
                && float.TryParse(commRating, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var cr))
            { item.CommunityRating = cr; changed = true; }

            if (d.TryGetValue("productionYear", out var year)
                && int.TryParse(year, out var yr))
            { item.ProductionYear = yr; changed = true; }

            if (d.TryGetValue("premiereDate", out var premiere)
                && DateTime.TryParse(premiere, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var pd))
            { item.PremiereDate = pd; changed = true; }

            if (d.TryGetValue("genres", out var genresJson) && !string.IsNullOrWhiteSpace(genresJson))
            {
                try
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(genresJson);
                    if (arr != null) { item.Genres = arr; changed = true; }
                }
                catch { /* ignore bad JSON */ }
            }

            if (d.TryGetValue("tags", out var tagsJson) && !string.IsNullOrWhiteSpace(tagsJson))
            {
                try
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(tagsJson);
                    if (arr != null) { item.Tags = arr; changed = true; }
                }
                catch { /* ignore bad JSON */ }
            }

            if (d.TryGetValue("providerIds", out var pidsJson) && !string.IsNullOrWhiteSpace(pidsJson))
            {
                try
                {
                    var pids = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(pidsJson);
                    if (pids != null)
                    {
                        foreach (var kv in pids)
                            item.ProviderIds[kv.Key] = kv.Value;
                        changed = true;
                    }
                }
                catch { /* ignore bad JSON */ }
            }

            if (!changed) return false;

            item.DateModified = DateTime.UtcNow;
            _library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit,
                System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] UpdateMetadata: saved changes to item {Id}", item.Id);
            return true;
        }

        /// <summary>
        /// Returns recent activity log entries.
        /// limit: max entries to return (default 50, max 500).
        /// userId: optional — filter to entries for a specific user ID.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetActivity(int limit = 50, string userId = null)
        {
            limit = Math.Max(1, Math.Min(limit, 500));

            var query = new ActivityLogQuery
            {
                HasUserId = !string.IsNullOrEmpty(userId) ? (bool?)true : null,
                MinDate = null
            };

            var result = _activity.GetPagedResultAsync(query).GetAwaiter().GetResult();
            var entries = result?.Items ?? Array.Empty<ActivityLogEntry>();

            Guid? filterUser = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                filterUser = ug;

            return entries
                .Where(e => filterUser == null || e.UserId == filterUser)
                .Take(limit)
                .Select(e => (object)new
                {
                    id = e.Id,
                    name = e.Name,
                    overview = e.Overview,
                    shortOverview = e.ShortOverview,
                    type = e.Type,
                    itemId = e.ItemId,
                    date = e.Date,
                    userId = e.UserId.ToString("N"),
                    severity = e.Severity.ToString()
                }).ToArray();
        }

        /// <summary>
        /// Returns all registered scheduled tasks with their current state.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetScheduledTasks()
        {
            return (_tasks.ScheduledTasks ?? Array.Empty<IScheduledTaskWorker>())
                .Select(t => (object)new
                {
                    id = t.Id,
                    name = t.Name,
                    description = t.Description,
                    category = t.Category,
                    state = t.State.ToString(),
                    progress = t.CurrentProgress,
                    lastResult = t.LastExecutionResult == null ? null : new
                    {
                        status = t.LastExecutionResult.Status.ToString(),
                        startTime = t.LastExecutionResult.StartTimeUtc,
                        endTime = t.LastExecutionResult.EndTimeUtc,
                        errorMessage = t.LastExecutionResult.ErrorMessage
                    }
                }).ToArray();
        }

        /// <summary>
        /// Queue a scheduled task by its ID. Returns false if the task was not found.
        /// Requires: jellyfin.tasks
        /// </summary>
        public bool RunScheduledTask(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return false;
            var task = (_tasks.ScheduledTasks ?? Array.Empty<IScheduledTaskWorker>())
                .FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.OrdinalIgnoreCase));
            if (task == null) return false;
            _tasks.QueueScheduledTask(task.ScheduledTask, new TaskOptions());
            _logger.LogInformation("[JellyFrame] RunScheduledTask: queued task '{Id}' ({Name})", taskId, task.Name);
            return true;
        }

        /// <summary>
        /// Returns all known devices, optionally filtered to a specific user.
        /// Requires: jellyfin.read
        /// </summary>
        public object[] GetDevices(string userId = null)
        {
            IEnumerable<MediaBrowser.Model.Devices.DeviceInfo> devices;

            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
            {
                var result = _devices.GetDevicesForUser(ug);
                devices = (result?.Items ?? Array.Empty<MediaBrowser.Model.Dto.DeviceInfoDto>())
                    .Select(d => new MediaBrowser.Model.Devices.DeviceInfo
                    {
                        Id = d.Id,
                        Name = d.Name,
                        CustomName = d.CustomName,
                        AppName = d.AppName,
                        AppVersion = d.AppVersion,
                        LastUserName = d.LastUserName,
                        LastUserId = d.LastUserId,
                        DateLastActivity = d.DateLastActivity
                    });
            }
            else
            {
                var result = _devices.GetDeviceInfos(new DeviceQuery());
                devices = result?.Items ?? Array.Empty<MediaBrowser.Model.Devices.DeviceInfo>();
            }

            return devices.Select(d => (object)new
            {
                id = d.Id,
                name = d.Name,
                customName = d.CustomName,
                appName = d.AppName,
                appVersion = d.AppVersion,
                lastUserName = d.LastUserName,
                lastUserId = d.LastUserId?.ToString("N"),
                dateLastActivity = d.DateLastActivity
            }).ToArray();
        }

        /// <summary>
        /// Delete a device by its string device ID. Returns false if not found.
        /// Requires: jellyfin.delete
        /// </summary>
        public bool DeleteDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            var query = new DeviceQuery { DeviceId = deviceId };
            var result = _devices.GetDevices(query);
            var entity = result?.Items?.FirstOrDefault();
            if (entity == null) return false;

            _devices.DeleteDevice(entity).GetAwaiter().GetResult();
            _logger.LogInformation("[JellyFrame] DeleteDevice: removed device '{Id}'", deviceId);
            return true;
        }

        // -----------------------------------------------------------------------
        // WebSocket messaging
        // -----------------------------------------------------------------------

        /// <summary>
        /// Send a general WebSocket command to a session.
        /// type: a GeneralCommandType string, e.g. "DisplayMessage", "GoHome", "MoveUp"
        /// arguments: optional key-value pairs passed as the command arguments
        /// Requires: jellyfin.write
        /// </summary>
        public Task SendWebSocketMessage(string sessionId, string type, object arguments = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(type))
                return Task.CompletedTask;

            if (!Enum.TryParse<GeneralCommandType>(type, ignoreCase: true, out var cmdType))
            {
                _logger.LogWarning("[JellyFrame] SendWebSocketMessage: unknown command type '{Type}'", type);
                return Task.CompletedTask;
            }

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (arguments is IDictionary<string, object> dictObj)
                foreach (var kv in dictObj)
                    if (kv.Value != null) args[kv.Key] = kv.Value.ToString();
                    else if (arguments is Jint.Native.Object.ObjectInstance jsObj)
                        foreach (var prop in jsObj.GetOwnProperties())
                        {
                            var v = prop.Value.Value?.ToString();
                            if (v != null && v != "null" && v != "undefined")
                                args[prop.Key.ToString()] = v;
                        }

            var cmd = new GeneralCommand(args);
            cmd.Name = cmdType;

            return _sessions.SendGeneralCommand(null, sessionId, cmd, CancellationToken.None);
        }

        /// <summary>
        /// Sends a non-intrusive JellyFrame notification to all active WebSocket sessions
        /// for the given user, or to ALL sessions if userId is null.
        ///
        /// The message is sent over the existing Jellyfin WebSocket connection with
        /// MessageType "JellyFrameNotification". The Jellyfin web client ignores unknown
        /// message types, so it passes through silently. A browser mod's jsUrl script can
        /// listen for it via:
        ///
        ///   ApiClient.addEventListener('message', function(e, msg) {
        ///     if (msg.MessageType === 'JellyFrameNotification') {
        ///       var d = msg.Data; // { title, body, type, modId, data }
        ///       // render your own toast here
        ///     }
        ///   });
        ///
        /// payload: object with any of { title, body, type, data }
        /// Requires: jellyfin.write
        /// </summary>
        public int Notify(string userId, object payload)
        {
            var title = "";
            var body = "";
            var type = "info";
            object extra = null;

            void ApplyPayload(IDictionary<string, object> d)
            {
                if (d.TryGetValue("title", out var t) && t != null) title = t.ToString();
                if (d.TryGetValue("body", out var b) && b != null) body = b.ToString();
                if (d.TryGetValue("type", out var tp) && tp != null) type = tp.ToString();
                if (d.TryGetValue("data", out var ex)) extra = ex;
            }

            if (payload is IDictionary<string, object> dictObj)
                ApplyPayload(dictObj);
            else if (payload is Jint.Native.Object.ObjectInstance jsObj)
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsObj.GetOwnProperties())
                {
                    var v = prop.Value.Value;
                    if (v != null && v.Type != Jint.Runtime.Types.Null && v.Type != Jint.Runtime.Types.Undefined)
                        d[prop.Key.ToString()] = v.ToObject();
                }
                ApplyPayload(d);
            }

            var msg = new JellyFrameNotificationMessage(title, body, type, extra);

            Guid? filterGuid = null;
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var ug))
                filterGuid = ug;

            var sessions = _sessions.Sessions
                .Where(s => filterGuid == null || s.UserId == filterGuid)
                .ToList();

            int sent = 0;
            foreach (var session in sessions)
            {
                foreach (var controller in session.SessionControllers)
                {
                    if (controller is not IWebSocketConnection ws) continue;
                    if (ws.State != System.Net.WebSockets.WebSocketState.Open) continue;
                    try
                    {
                        using var wsCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                        ws.SendAsync(msg, wsCts.Token).GetAwaiter().GetResult();
                        sent++;
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("[JellyFrame] Notify: send to session {Id} timed out", session.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex,
                            "[JellyFrame] Notify: failed to send to session {Id}", session.Id);
                    }
                }
            }

            _logger.LogDebug("[JellyFrame] Notify: sent to {Count} WebSocket session(s) (userId={UserId})",
                sent, userId ?? "all");
            return sent;
        }

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

            // People / cast — only populated for items that carry cast info.
            // Each entry: { name, role, type, personId, imageTag }
            object[] people = null;
            try
            {
                var rawPeople = _library.GetPeople(new InternalPeopleQuery { ItemId = item.Id });
                if (rawPeople != null && rawPeople.Count > 0)
                {
                    people = rawPeople.Select(person => (object)new
                    {
                        name = person.Name,
                        role = person.Role,
                        type = person.Type.ToString(),
                        personId = (string)null  // lightweight — use GetItemPeople for full details
                    }).ToArray();
                }
            }
            catch { people = null; }

            // Studios
            string[] studios = null;
            try { studios = item.Studios; } catch { }

            // Critic / audience ratings (Rotten Tomatoes etc. via providers)
            float? criticRating = null;
            try { criticRating = item.CriticRating; } catch { }

            // Series-specific fields
            string seriesId = null;
            string seasonId = null;
            int? seasonNumber = null;
            try
            {
                if (item is MediaBrowser.Controller.Entities.TV.Episode ep)
                {
                    seriesId = ep.SeriesId.ToString("N");
                    seasonId = ep.SeasonId.ToString("N");
                    seasonNumber = ep.ParentIndexNumber;
                }
            }
            catch { }

            // Collection/box-set membership
            string[] collectionIds = null;
            try
            {
                var parents = item.GetParents();
                collectionIds = parents
                    .Where(p => p is MediaBrowser.Controller.Entities.Movies.BoxSet)
                    .Select(p => p.Id.ToString("N"))
                    .ToArray();
            }
            catch { }

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
                criticRating = criticRating,
                genres = item.Genres,
                tags = item.Tags,
                studios = studios,
                people = people,
                providerIds = item.ProviderIds,
                parentId = item.ParentId.ToString("N"),
                productionYear = item.ProductionYear,
                runTimeTicks = item.RunTimeTicks,
                isFavorite = isFavorite,
                dateCreated = item.DateCreated,
                dateModified = item.DateModified,
                // TV-specific
                seriesName = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeriesName,
                seasonName = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeasonName,
                seriesId = seriesId,
                seasonId = seasonId,
                indexNumber = item.IndexNumber,
                seasonNumber = seasonNumber,
                // Collections
                collectionIds = collectionIds,
                // Images
                imageTags = item.ImageInfos == null ? null : new
                {
                    Primary = item.ImageInfos.FirstOrDefault(i => i.Type == MediaBrowser.Model.Entities.ImageType.Primary) is var p && p != null ? GetTag(p) : null,
                    Thumb = item.ImageInfos.FirstOrDefault(i => i.Type == MediaBrowser.Model.Entities.ImageType.Thumb) is var th && th != null ? GetTag(th) : null,
                    Banner = item.ImageInfos.FirstOrDefault(i => i.Type == MediaBrowser.Model.Entities.ImageType.Banner) is var bn && bn != null ? GetTag(bn) : null,
                    Logo = item.ImageInfos.FirstOrDefault(i => i.Type == MediaBrowser.Model.Entities.ImageType.Logo) is var l && l != null ? GetTag(l) : null,
                    Backdrop = item.ImageInfos.FirstOrDefault(i => i.Type == MediaBrowser.Model.Entities.ImageType.Backdrop) is var bd && bd != null ? GetTag(bd) : null
                },
                backdropImageTags = item.ImageInfos == null ? Array.Empty<string>()
                    : item.ImageInfos
                        .Where(i => i.Type == MediaBrowser.Model.Entities.ImageType.Backdrop)
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