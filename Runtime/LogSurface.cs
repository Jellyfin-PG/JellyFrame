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

        public void Info(string message)
        {
            _logger.LogInformation("[Mod:{ModId}] {Message}", _modId, message);
            Append("info", message);
        }

        public void Debug(string message)
        {
            _logger.LogDebug("[Mod:{ModId}] {Message}", _modId, message);
            Append("debug", message);
        }

        public void Warn(string message)
        {
            _logger.LogWarning("[Mod:{ModId}] {Message}", _modId, message);
            Append("warn", message);
        }

        public void Error(string message)
        {
            _logger.LogError("[Mod:{ModId}] {Message}", _modId, message);
            Append("error", message);
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
                        time = e.Time.ToString("o")
                    };
                }
                return result;
            }
        }

        private void Append(string level, string message)
        {
            lock (_recentLock)
            {
                if (_recent.Count >= MaxEntries)
                    _recent.Dequeue();
                _recent.Enqueue(new LogEntry(level, message, DateTime.UtcNow));
            }
        }

        private readonly struct LogEntry
        {
            public string Level { get; }
            public string Message { get; }
            public DateTime Time { get; }
            public LogEntry(string level, string message, DateTime time)
            { Level = level; Message = message; Time = time; }
        }
    }
}
