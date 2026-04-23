using System;
using System.Threading.Tasks;
using Jint.Native;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{

    public class GatedHttpSurface
    {
        private readonly HttpSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedHttpSurface(HttpSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public HttpSurface.HttpResult Get(string url, object options = null)
        { _perms.Require(PermissionSurface.Http); return _inner.Get(url, options); }

        public HttpSurface.HttpResult Post(string url, string body = null, object options = null)
        { _perms.Require(PermissionSurface.Http); return _inner.Post(url, body, options); }

        public HttpSurface.HttpResult Put(string url, string body = null, object options = null)
        { _perms.Require(PermissionSurface.Http); return _inner.Put(url, body, options); }

        public HttpSurface.HttpResult Delete(string url, object options = null)
        { _perms.Require(PermissionSurface.Http); return _inner.Delete(url, options); }

        public HttpSurface.HttpResult Patch(string url, string body = null, object options = null)
        { _perms.Require(PermissionSurface.Http); return _inner.Patch(url, body, options); }
    }

    public class GatedStoreSurface
    {
        private readonly StoreSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedStoreSurface(StoreSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public string Get(string key) { _perms.Require(PermissionSurface.Store); return _inner.Get(key); }
        public void Set(string key, string v) { _perms.Require(PermissionSurface.Store); _inner.Set(key, v); }
        public void Delete(string key) { _perms.Require(PermissionSurface.Store); _inner.Delete(key); }
        public void Clear() { _perms.Require(PermissionSurface.Store); _inner.Clear(); }
        public string[] Keys() { _perms.Require(PermissionSurface.Store); return _inner.Keys(); }
    }

    public class GatedUserStoreSurface
    {
        private readonly UserStoreSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedUserStoreSurface(UserStoreSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public string Get(string uid, string key) { _perms.Require(PermissionSurface.Store); return _inner.Get(uid, key); }
        public void Set(string uid, string key, string v) { _perms.Require(PermissionSurface.Store); _inner.Set(uid, key, v); }
        public void Delete(string uid, string key) { _perms.Require(PermissionSurface.Store); _inner.Delete(uid, key); }
        public void Clear(string uid) { _perms.Require(PermissionSurface.Store); _inner.Clear(uid); }
        public string[] Keys(string uid) { _perms.Require(PermissionSurface.Store); return _inner.Keys(uid); }
        public string[] Users() { _perms.Require(PermissionSurface.Store); return _inner.Users(); }
    }

    public class GatedSchedulerSurface
    {
        private readonly SchedulerSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedSchedulerSurface(SchedulerSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public string Interval(double ms, JsValue handler)
        { _perms.Require(PermissionSurface.Scheduler); return _inner.Interval(ms, handler); }

        public string Cron(string expr, JsValue handler)
        { _perms.Require(PermissionSurface.Scheduler); return _inner.Cron(expr, handler); }

        public void Cancel(string id) => _inner.Cancel(id);
        public void CancelAll() => _inner.CancelAll();
        public int Count => _inner.Count;
    }

    public class GatedWebhookSurface
    {
        private readonly WebhookSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedWebhookSurface(WebhookSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public void Register(string name, JsValue handler) { _perms.Require(PermissionSurface.Webhooks); _inner.Register(name, handler); }
        public void Unregister(string name) { _perms.Require(PermissionSurface.Webhooks); _inner.Unregister(name); }
        public string[] List() => _inner.List();

        public WebhookSurface.WebhookResult Send(string url, object payload, object options = null)
        { _perms.Require(PermissionSurface.Webhooks); return _inner.Send(url, payload, options); }
    }

    public class GatedRpcSurface
    {
        private readonly RpcSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedRpcSurface(RpcSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public void Handle(string method, JsValue handler) { _perms.Require(PermissionSurface.Rpc); _inner.Handle(method, handler); }
        public void Unhandle(string method) { _perms.Require(PermissionSurface.Rpc); _inner.Unhandle(method); }
        public RpcSurface.RpcResult Call(string target, string method, object payload = null, double timeoutMs = 5000)
        { _perms.Require(PermissionSurface.Rpc); return _inner.Call(target, method, payload, timeoutMs); }
        public string[] Methods() => _inner.Methods();
    }

    public class GatedEventBusSurface
    {
        private readonly EventBusSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedEventBusSurface(EventBusSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public int Emit(string eventName, object data = null) { _perms.Require(PermissionSurface.Bus); return _inner.Emit(eventName, data); }
        public string On(string eventName, JsValue handler) { _perms.Require(PermissionSurface.Bus); return _inner.On(eventName, handler); }
        public void Off(string subscriptionId) { _perms.Require(PermissionSurface.Bus); _inner.Off(subscriptionId); }
        public void OffAll() { _perms.Require(PermissionSurface.Bus); _inner.OffAll(); }
    }

    public class GatedJellyfinSurface
    {
        private readonly JellyfinSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedJellyfinSurface(JellyfinSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public object GetItem(string id, string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetItem(id, userId); }
        public object[] GetItems(object q) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetItems(q); }
        public object[] GetItemsByIds(object ids, string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetItemsByIds(ids, userId); }
        public object GetItemByPath(string path) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetItemByPath(path); }
        public object[] Search(string term, int limit = 20) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.Search(term, limit); }
        public object[] GetLatestItems(string userId, int limit = 20) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetLatestItems(userId, limit); }
        public object[] GetResumeItems(string userId, int limit = 10) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetResumeItems(userId, limit); }
        public object[] GetUserLibraries(string userId) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUserLibraries(userId); }
        public object[] GetUsers() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUsers(); }
        public object GetUser(string id) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUser(id); }
        public object GetUserByName(string name) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUserByName(name); }
        public object[] GetSessions() { _perms.Require(PermissionSurface.JellyfinAdmin); return _inner.GetSessions(); }
        public object[] GetSessionsForUser(string userId) { _perms.Require(PermissionSurface.JellyfinAdmin); return _inner.GetSessionsForUser(userId); }
        public object[] GetPlaylists(string userId) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetPlaylists(userId); }
        public object[] GetCollections(string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetCollections(userId); }
        public object[] GetLibraries() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetLibraries(); }
        public int GetItemCount(object query) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetItemCount(query); }
        public object[] GetGenres(string parentId = null, string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetGenres(parentId, userId); }
        public object[] GetStudios(string parentId = null, string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetStudios(parentId, userId); }
        public object GetPerson(string name) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetPerson(name); }
        public object[] GetPersonItems(string personName, string userId = null, string itemType = null, int limit = 50) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetPersonItems(personName, userId, itemType, limit); }
        public object[] GetNextUp(string userId, int limit = 20, string seriesId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetNextUp(userId, limit, seriesId); }
        public object[] GetSimilarItems(string itemId, string userId = null, int limit = 12) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetSimilarItems(itemId, userId, limit); }
        public object GetItemCounts(string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetItemCounts(userId); }
        public object[] GetSubtitleProviders() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetSubtitleProviders(); }
        public string GetEncoderVersion() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetEncoderVersion(); }
        public object GetEncoderInfo() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetEncoderInfo(); }
        public object[] GetActivity(int limit = 50, string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetActivity(limit, userId); }
        public object[] GetScheduledTasks() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetScheduledTasks(); }
        public object[] GetDevices(string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetDevices(userId); }

        public bool UpdateMetadata(string itemId, object fields) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.UpdateMetadata(itemId, fields); }
        public bool SetImage(string itemId, string imageType, string url) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SetImage(itemId, imageType, url); }
        public System.Threading.Tasks.Task SendWebSocketMessage(string sessionId, string type, object arguments = null) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SendWebSocketMessage(sessionId, type, arguments); }
        public int Notify(string userId, object payload) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.Notify(userId, payload); }
        public bool RefreshMetadata(string itemId, bool replaceAll = false) { _perms.Require(PermissionSurface.JellyfinRefresh); return _inner.RefreshMetadata(itemId, replaceAll); }
        public bool DeleteItem(string itemId) { _perms.Require(PermissionSurface.JellyfinDelete); return _inner.DeleteItem(itemId); }
        public bool DeleteDevice(string deviceId) { _perms.Require(PermissionSurface.JellyfinDelete); return _inner.DeleteDevice(deviceId); }
        public bool RunScheduledTask(string taskId) { _perms.Require(PermissionSurface.JellyfinTasks); return _inner.RunScheduledTask(taskId); }

        public object GetUserData(string itemId, string userId) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUserData(itemId, userId); }
        public bool MarkPlayed(string itemId, string userId) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.MarkPlayed(itemId, userId); }
        public bool MarkUnplayed(string itemId, string userId) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.MarkUnplayed(itemId, userId); }
        public bool SetFavourite(string itemId, string userId, bool isFavourite) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SetFavourite(itemId, userId, isFavourite); }
        public bool SetRating(string itemId, string userId, double? rating) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SetRating(itemId, userId, rating); }

        public object GetPlaybackInfo(string itemId, string userId) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetPlaybackInfo(itemId, userId); }
        public bool ReportPlaybackStart(string sessionId, string itemId, string mediaSourceId = null, int? audioStreamIndex = null, int? subtitleStreamIndex = null) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.ReportPlaybackStart(sessionId, itemId, mediaSourceId, audioStreamIndex, subtitleStreamIndex); }
        public bool ReportPlaybackProgress(string sessionId, string itemId, long? positionTicks = null, bool isPaused = false, string mediaSourceId = null, string playSessionId = null) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.ReportPlaybackProgress(sessionId, itemId, positionTicks, isPaused, mediaSourceId, playSessionId); }
        public bool ReportPlaybackStopped(string sessionId, string itemId, long? positionTicks = null, bool failed = false, string mediaSourceId = null, string playSessionId = null) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.ReportPlaybackStopped(sessionId, itemId, positionTicks, failed, mediaSourceId, playSessionId); }

        public bool SetTags(string itemId, object tags) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SetTags(itemId, tags); }
        public bool SetOfficialRating(string itemId, string rating) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SetOfficialRating(itemId, rating); }
        public string CreateCollection(string name, object itemIds = null) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.CreateCollection(name, itemIds); }
        public bool AddToCollection(string collectionId, object itemIds) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.AddToCollection(collectionId, itemIds); }
        public string CreatePlaylist(string name, object itemIds = null, string userId = null) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.CreatePlaylist(name, itemIds, userId); }
        public bool AddToPlaylist(string playlistId, object itemIds, string userId = null) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.AddToPlaylist(playlistId, itemIds, userId); }
        public void ScanLibrary() { _perms.Require(PermissionSurface.JellyfinTasks); _inner.ScanLibrary(); }

        public object[] GetActiveSessions() { _perms.Require(PermissionSurface.JellyfinAdmin); return _inner.GetActiveSessions(); }
        public bool TerminateSession(string sessionId) { _perms.Require(PermissionSurface.JellyfinAdmin); return _inner.TerminateSession(sessionId); }
        public string CreateUser(string username) { _perms.Require(PermissionSurface.JellyfinAdmin); return _inner.CreateUser(username); }
        public bool DeleteUser(string userId) { _perms.Require(PermissionSurface.JellyfinAdmin); return _inner.DeleteUser(userId); }
        public bool ResetUserPassword(string userId) { _perms.Require(PermissionSurface.JellyfinAdmin); return _inner.ResetUserPassword(userId); }

        public System.Threading.Tasks.Task SendMessageToSession(string s, string h, string t, int ms = 5000) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SendMessageToSession(s, h, t, ms); }
        public System.Threading.Tasks.Task SendMessageToAllSessions(string h, string t, int ms = 5000) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SendMessageToAllSessions(h, t, ms); }
        public System.Threading.Tasks.Task StopPlayback(string s) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.StopPlayback(s); }
        public System.Threading.Tasks.Task PausePlayback(string s) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.PausePlayback(s); }
        public System.Threading.Tasks.Task ResumePlayback(string s) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.ResumePlayback(s); }
        public System.Threading.Tasks.Task SeekPlayback(string s, long t) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SeekPlayback(s, t); }
        public System.Threading.Tasks.Task PlayItem(string s, string i) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.PlayItem(s, i); }
        public System.Threading.Tasks.Task DownloadSubtitles(string i, int idx) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.DownloadSubtitles(i, idx); }

        public void On(string evt, Func<object, System.Threading.Tasks.Task> h) => _inner.On(evt, h);
        public void Off(string evt) => _inner.Off(evt);

        public object[] GetChannels(string userId = null, int limit = 100, int startIndex = 0) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetChannels(userId, limit, startIndex); }
        public object[] GetPrograms(object query, string userId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetPrograms(query, userId); }
        public object[] GetRecordings(string userId = null, string channelId = null, bool? isInProgress = null, int limit = 50, int startIndex = 0) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetRecordings(userId, channelId, isInProgress, limit, startIndex); }
        public object[] GetTimers(string channelId = null, string seriesTimerId = null) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetTimers(channelId, seriesTimerId); }
        public object[] GetSeriesTimers() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetSeriesTimers(); }
        public object GetLiveTvInfo() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetLiveTvInfo(); }

        public bool CreateTimer(string programId, int prePaddingSeconds = 0, int postPaddingSeconds = 0) { _perms.Require(PermissionSurface.JellyfinLiveTv); return _inner.CreateTimer(programId, prePaddingSeconds, postPaddingSeconds); }
        public bool CreateSeriesTimer(string programId, bool recordNewOnly = true, bool recordAnyChannel = false, int prePaddingSeconds = 0, int postPaddingSeconds = 0) { _perms.Require(PermissionSurface.JellyfinLiveTv); return _inner.CreateSeriesTimer(programId, recordNewOnly, recordAnyChannel, prePaddingSeconds, postPaddingSeconds); }
        public bool CancelTimer(string timerId) { _perms.Require(PermissionSurface.JellyfinLiveTv); return _inner.CancelTimer(timerId); }
        public bool CancelSeriesTimer(string seriesTimerId) { _perms.Require(PermissionSurface.JellyfinLiveTv); return _inner.CancelSeriesTimer(seriesTimerId); }
        public System.Threading.Tasks.Task FireEvent(string evt, object data) => _inner.FireEvent(evt, data);
    }

    public class GatedKvSurface
    {
        private readonly KvSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedKvSurface(KvSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public void Set(string key, string value) { _perms.Require(PermissionSurface.SharedStore); _inner.Set(key, value); }
        public string Get(string key) { _perms.Require(PermissionSurface.SharedStore); return _inner.Get(key); }
        public void Delete(string key) { _perms.Require(PermissionSurface.SharedStore); _inner.Delete(key); }
        public string[] Keys() { _perms.Require(PermissionSurface.SharedStore); return _inner.Keys(); }
        public string[] AllKeys() { _perms.Require(PermissionSurface.SharedStore); return _inner.AllKeys(); }
    }

    public class GatedFileSystemSurface
    {
        private readonly FileSystemSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedFileSystemSurface(FileSystemSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public string ReadFile(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.ReadFile(path); }
        public string ReadFileBase64(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.ReadFileBase64(path); }
        public void WriteFile(string path, string content) { _perms.Require(PermissionSurface.Filesystem); _inner.WriteFile(path, content); }
        public void AppendFile(string path, string content) { _perms.Require(PermissionSurface.Filesystem); _inner.AppendFile(path, content); }
        public void WriteFileBase64(string path, string base64) { _perms.Require(PermissionSurface.Filesystem); _inner.WriteFileBase64(path, base64); }
        public bool DeleteFile(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.DeleteFile(path); }
        public void MoveFile(string source, string dest) { _perms.Require(PermissionSurface.Filesystem); _inner.MoveFile(source, dest); }
        public void CopyFile(string source, string dest) { _perms.Require(PermissionSurface.Filesystem); _inner.CopyFile(source, dest); }
        public object[] ListDir(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.ListDir(path); }
        public void MakeDir(string path) { _perms.Require(PermissionSurface.Filesystem); _inner.MakeDir(path); }
        public bool DeleteDir(string path, bool recursive = false) { _perms.Require(PermissionSurface.Filesystem); return _inner.DeleteDir(path, recursive); }
        public bool Exists(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.Exists(path); }
        public bool IsFile(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.IsFile(path); }
        public bool IsDir(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.IsDir(path); }
        public object Stat(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.Stat(path); }
        public string ResolvePath(string path) { _perms.Require(PermissionSurface.Filesystem); return _inner.ResolvePath(path); }
        public string JoinPath(string a, string b) { _perms.Require(PermissionSurface.Filesystem); return _inner.JoinPath(a, b); }
    }

    public class GatedOsSurface
    {
        private readonly OsSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedOsSurface(OsSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public OsSurface.ExecResult Exec(string command, object options = null) { _perms.Require(PermissionSurface.Os); return _inner.Exec(command, options); }
        public string Env(string name) { _perms.Require(PermissionSurface.Os); return _inner.Env(name); }
        public object EnvAll() { _perms.Require(PermissionSurface.Os); return _inner.EnvAll(); }
        public string Platform() { _perms.Require(PermissionSurface.Os); return _inner.Platform(); }
        public string OsDescription() { _perms.Require(PermissionSurface.Os); return _inner.OsDescription(); }
        public string Hostname() { _perms.Require(PermissionSurface.Os); return _inner.Hostname(); }
        public int CpuCount() { _perms.Require(PermissionSurface.Os); return _inner.CpuCount(); }
        public object MemoryInfo() { _perms.Require(PermissionSurface.Os); return _inner.MemoryInfo(); }
    }

    public class GatedDbSurface
    {
        private readonly DbSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedDbSurface(DbSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public DbTable Table(string name) { _perms.Require(PermissionSurface.Db); return _inner.Table(name); }
        public string[] Tables() { _perms.Require(PermissionSurface.Db); return _inner.Tables(); }
        public bool HasTable(string name) { _perms.Require(PermissionSurface.Db); return _inner.HasTable(name); }
        public bool DropTable(string name) { _perms.Require(PermissionSurface.Db); return _inner.DropTable(name); }

        public DbQuery Query(string tableName) { _perms.Require(PermissionSurface.Db); return _inner.Query(tableName); }

        public void Exec(string sql, object parameters = null) { _perms.Require(PermissionSurface.Db); _inner.Exec(sql, parameters); }
        public object Run(string sql, object parameters = null) { _perms.Require(PermissionSurface.Db); return _inner.Run(sql, parameters); }
        public object[] QueryRaw(string sql, object parameters = null) { _perms.Require(PermissionSurface.Db); return _inner.QueryRaw(sql, parameters); }
        public object Transaction(Jint.Native.JsValue fn) { _perms.Require(PermissionSurface.Db); return _inner.Transaction(fn); }
    }

    /// <summary>
    /// Gated wrapper for the shared (cross-mod) SQLite database surface.
    /// Requires the <c>db.shared</c> permission.
    /// Tables have no automatic prefix — all mods with this permission
    /// share the same table namespace.
    /// </summary>
    public class GatedSharedDbSurface
    {
        private readonly DbSurface _inner;
        private readonly PermissionSurface _perms;

        public GatedSharedDbSurface(DbSurface inner, PermissionSurface perms)
        {
            _inner = inner;
            _perms = perms;
        }

        public DbTable Table(string name) { _perms.Require(PermissionSurface.DbShared); return _inner.Table(name); }
        public string[] Tables() { _perms.Require(PermissionSurface.DbShared); return _inner.Tables(); }
        public bool HasTable(string name) { _perms.Require(PermissionSurface.DbShared); return _inner.HasTable(name); }
        public bool DropTable(string name) { _perms.Require(PermissionSurface.DbShared); return _inner.DropTable(name); }

        public DbQuery Query(string tableName) { _perms.Require(PermissionSurface.DbShared); return _inner.Query(tableName); }

        public void Exec(string sql, object parameters = null) { _perms.Require(PermissionSurface.DbShared); _inner.Exec(sql, parameters); }
        public object Run(string sql, object parameters = null) { _perms.Require(PermissionSurface.DbShared); return _inner.Run(sql, parameters); }
        public object[] QueryRaw(string sql, object parameters = null) { _perms.Require(PermissionSurface.DbShared); return _inner.QueryRaw(sql, parameters); }
        public object Transaction(Jint.Native.JsValue fn) { _perms.Require(PermissionSurface.DbShared); return _inner.Transaction(fn); }
    }

}