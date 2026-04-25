using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class LogSurface
    {
        private readonly ILogger _logger;
        private readonly string _modId;

        private const int MaxEntries = 200;
        private readonly Queue<LogEntry> _recent = new();
        private readonly object _recentLock = new();

        public LogSurface(ILogger logger, string modId)
        {
            _logger = logger;
            _modId = modId;
        }

        public void Info(string message, object data = null)
        {
            if (data == null)
                _logger.LogInformation("[Mod:{ModId}] {Message}", _modId, message);
            else
                _logger.LogInformation("[Mod:{ModId}] {Message} {@Data}", _modId, message, data);
            Append("info", message, data);
        }

        public void Debug(string message, object data = null)
        {
            if (data == null)
                _logger.LogDebug("[Mod:{ModId}] {Message}", _modId, message);
            else
                _logger.LogDebug("[Mod:{ModId}] {Message} {@Data}", _modId, message, data);
            Append("debug", message, data);
        }

        public void Warn(string message, object data = null)
        {
            if (data == null)
                _logger.LogWarning("[Mod:{ModId}] {Message}", _modId, message);
            else
                _logger.LogWarning("[Mod:{ModId}] {Message} {@Data}", _modId, message, data);
            Append("warn", message, data);
        }

        public void Error(string message, object data = null)
        {
            if (data == null)
                _logger.LogError("[Mod:{ModId}] {Message}", _modId, message);
            else
                _logger.LogError("[Mod:{ModId}] {Message} {@Data}", _modId, message, data);
            Append("error", message, data);
        }

        /// <summary>
        /// Return the most recent n log entries emitted by this mod.
        /// Each entry: { level, message, time (ISO 8601 string) }
        /// n is clamped to [1, 200].
        /// </summary>
        public object[] GetRecent(int n = 50)
        {
            n = Math.Max(1, Math.Min(n, MaxEntries));
            lock (_recentLock)
            {
                var arr = _recent.ToArray();
                var start = Math.Max(0, arr.Length - n);
                var result = new object[arr.Length - start];
                for (int i = start; i < arr.Length; i++)
                {
                    var e = arr[i];
                    result[i - start] = new
                    {
                        level = e.Level,
                        message = e.Message,
                        data = e.Data,
                        time = e.Time.ToString("o")
                    };
                }
                return result;
            }
        }

        private void Append(string level, string message, object data = null)
        {
            // Serialize JS objects to a plain dictionary so they survive outside
            // the Jint context and can be JSON-serialised by the health endpoint.
            object safeData = null;
            if (data != null)
            {
                try
                {
                    if (data is Jint.Native.Object.ObjectInstance jsObj)
                    {
                        var dict = new Dictionary<string, object>();
                        foreach (var prop in jsObj.GetOwnProperties())
                            dict[prop.Key.ToString()] = prop.Value.Value?.ToObject();
                        safeData = dict;
                    }
                    else if (data is IDictionary<string, object> d)
                    {
                        safeData = new Dictionary<string, object>(d);
                    }
                    else
                    {
                        safeData = data.ToString();
                    }
                }
                catch { safeData = "[unserializable]"; }
            }

            lock (_recentLock)
            {
                if (_recent.Count >= MaxEntries)
                    _recent.Dequeue();
                _recent.Enqueue(new LogEntry(level, message, safeData, DateTime.UtcNow));
            }
        }

        private readonly struct LogEntry
        {
            public string Level { get; }
            public string Message { get; }
            public object Data { get; }
            public DateTime Time { get; }
            public LogEntry(string level, string message, object data, DateTime time)
            { Level = level; Message = message; Data = data; Time = time; }
        }
    }
}
