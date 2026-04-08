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
        public object GetItemByPath(string path) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetItemByPath(path); }
        public object[] Search(string term, int limit = 20) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.Search(term, limit); }
        public object[] GetLatestItems(string userId, int limit = 20) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetLatestItems(userId, limit); }
        public object[] GetResumeItems(string userId, int limit = 10) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetResumeItems(userId, limit); }
        public object[] GetUserLibraries(string userId) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUserLibraries(userId); }
        public object[] GetUsers() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUsers(); }
        public object GetUser(string id) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUser(id); }
        public object GetUserByName(string name) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetUserByName(name); }
        public object[] GetSessions() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetSessions(); }
        public object[] GetSessionsForUser(string userId) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetSessionsForUser(userId); }
        public object[] GetPlaylists(string userId) { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetPlaylists(userId); }
        public object[] GetSubtitleProviders() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetSubtitleProviders(); }
        public string GetEncoderVersion() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetEncoderVersion(); }
        public object GetEncoderInfo() { _perms.Require(PermissionSurface.JellyfinRead); return _inner.GetEncoderInfo(); }

        public bool UpdateMetadata(string itemId, object fields) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.UpdateMetadata(itemId, fields); }
        public bool SetFavorite(string uid, string iid, bool fav) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SetFavorite(uid, iid, fav); }
        public bool SetWatched(string uid, string iid, bool w) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SetWatched(uid, iid, w); }
        public void RefreshLibrary() { _perms.Require(PermissionSurface.JellyfinWrite); _inner.RefreshLibrary(); }
        public System.Threading.Tasks.Task SendMessageToSession(string s, string h, string t, int ms = 5000) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SendMessageToSession(s, h, t, ms); }
        public System.Threading.Tasks.Task SendMessageToAllSessions(string h, string t, int ms = 5000) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SendMessageToAllSessions(h, t, ms); }
        public System.Threading.Tasks.Task StopPlayback(string s) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.StopPlayback(s); }
        public System.Threading.Tasks.Task PausePlayback(string s) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.PausePlayback(s); }
        public System.Threading.Tasks.Task ResumePlayback(string s) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.ResumePlayback(s); }
        public System.Threading.Tasks.Task SeekPlayback(string s, long t) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.SeekPlayback(s, t); }
        public System.Threading.Tasks.Task PlayItem(string s, string i) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.PlayItem(s, i); }
        public System.Threading.Tasks.Task<string> CreatePlaylist(string u, string n, string[] ids) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.CreatePlaylist(u, n, ids); }
        public System.Threading.Tasks.Task DownloadSubtitles(string i, int idx) { _perms.Require(PermissionSurface.JellyfinWrite); return _inner.DownloadSubtitles(i, idx); }

        public void On(string evt, Func<object, System.Threading.Tasks.Task> h) { _perms.Require(PermissionSurface.JellyfinRead); _inner.On(evt, h); }
        public void Off(string evt) => _inner.Off(evt);
        public System.Threading.Tasks.Task FireEvent(string evt, object data) => _inner.FireEvent(evt, data);
    }
}