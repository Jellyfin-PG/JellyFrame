using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public sealed class JellyfinEventSurface : IDisposable
    {
        private readonly ILibraryManager   _library;
        private readonly ISessionManager   _sessions;
        private readonly IUserDataManager  _userData;
        private readonly ILogger           _logger;
        private readonly Func<string, object, Task> _dispatch;
        private bool _disposed;

        public JellyfinEventSurface(
            ILibraryManager   library,
            ISessionManager   sessions,
            IUserDataManager  userData,
            ILogger           logger,
            Func<string, object, Task> dispatch)
        {
            _library  = library;
            _sessions = sessions;
            _userData = userData;
            _logger   = logger;
            _dispatch = dispatch;

            _library.ItemAdded   += OnItemAdded;
            _library.ItemUpdated += OnItemUpdated;
            _library.ItemRemoved += OnItemRemoved;

            _sessions.PlaybackStart    += OnPlaybackStart;
            _sessions.PlaybackProgress += OnPlaybackProgress;
            _sessions.PlaybackStopped  += OnPlaybackStopped;

            _userData.UserDataSaved += OnUserDataSaved;

            _logger.LogInformation("[JellyFrame] JellyfinEventSurface subscribed to library + session + userdata events");
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
            => Fire("item.added", new {
                itemId   = e.Item?.Id.ToString("N"),
                itemName = e.Item?.Name,
                itemType = e.Item?.GetBaseItemKind().ToString(),
                path     = e.Item?.Path
            });

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
            => Fire("item.updated", new {
                itemId   = e.Item?.Id.ToString("N"),
                itemName = e.Item?.Name,
                itemType = e.Item?.GetBaseItemKind().ToString()
            });

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
            => Fire("item.removed", new {
                itemId   = e.Item?.Id.ToString("N"),
                itemName = e.Item?.Name,
                itemType = e.Item?.GetBaseItemKind().ToString()
            });

        private void OnPlaybackStart(object sender, PlaybackProgressEventArgs e)
            => Fire("playback.started", new {
                sessionId  = e.Session?.Id,
                userId     = e.Session?.UserId.ToString("N"),
                userName   = e.Session?.UserName,
                itemId     = e.Item?.Id.ToString("N"),
                itemName   = e.Item?.Name,
                itemType   = e.Item?.GetBaseItemKind().ToString(),
                clientName = e.Session?.Client,
                deviceName = e.Session?.DeviceName,
                mediaSourceId  = e.MediaSourceId,
                playSessionId  = e.PlaySessionId
            });

        private void OnPlaybackProgress(object sender, PlaybackProgressEventArgs e)
            => Fire("playback.progress", new {
                sessionId         = e.Session?.Id,
                userId            = e.Session?.UserId.ToString("N"),
                itemId            = e.Item?.Id.ToString("N"),
                positionTicks     = e.PlaybackPositionTicks,
                isPaused          = e.IsPaused,
                mediaSourceId     = e.MediaSourceId,
                playSessionId     = e.PlaySessionId,
                isAutomated       = e.IsAutomated
            });

        private void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
            => Fire("playback.stopped", new {
                sessionId       = e.Session?.Id,
                userId          = e.Session?.UserId.ToString("N"),
                userName        = e.Session?.UserName,
                itemId          = e.Item?.Id.ToString("N"),
                itemName        = e.Item?.Name,
                positionTicks   = e.PlaybackPositionTicks,
                playedToEnd     = e.PlayedToCompletion,
                mediaSourceId   = e.MediaSourceId,
                playSessionId   = e.PlaySessionId
            });

        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
            => Fire("user.data.changed", new {
                userId      = e.UserId.ToString("N"),
                itemId      = e.Item?.Id.ToString("N"),
                itemName    = e.Item?.Name,
                saveReason  = e.SaveReason.ToString(),
                played      = e.UserData?.Played,
                isFavorite  = e.UserData?.IsFavorite,
                rating      = e.UserData?.Rating,
                positionTicks = e.UserData?.PlaybackPositionTicks,
                playCount   = e.UserData?.PlayCount
            });

        private void Fire(string eventName, object data)
        {
            if (_disposed) return;
            _ = _dispatch(eventName, data).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception,
                        "[JellyFrame] JellyfinEventSurface: error dispatching '{Event}'", eventName);
            }, TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _library.ItemAdded   -= OnItemAdded;
            _library.ItemUpdated -= OnItemUpdated;
            _library.ItemRemoved -= OnItemRemoved;

            _sessions.PlaybackStart    -= OnPlaybackStart;
            _sessions.PlaybackProgress -= OnPlaybackProgress;
            _sessions.PlaybackStopped  -= OnPlaybackStopped;

            _userData.UserDataSaved -= OnUserDataSaved;

            _logger.LogInformation("[JellyFrame] JellyfinEventSurface unsubscribed");
        }
    }
}
