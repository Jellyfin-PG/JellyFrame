using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    public class SchedulerSurface : IDisposable
    {
        private readonly string _modId;
        private readonly ILogger _logger;
        private Engine _engine;

        private readonly ConcurrentDictionary<string, Timer> _tasks = new();
        private int _idCounter;
        private bool _disposed;

        public SchedulerSurface(string modId, ILogger logger)
        {
            _modId = modId;
            _logger = logger;
        }

        public void SetEngine(Engine engine) => _engine = engine;

        public string Interval(double intervalMs, JsValue handler)
        {
            if (_disposed) return null;
            var id = NextId();
            var delay = TimeSpan.FromMilliseconds(Math.Max(intervalMs, 100));
            var timer = new Timer(_ => Invoke(id, handler), null, delay, delay);
            _tasks[id] = timer;
            _logger.LogDebug("[JellyFrame] Scheduler [{Mod}] interval task '{Id}' registered ({Ms}ms)",
                _modId, id, (int)intervalMs);
            return id;
        }

        public string Cron(string expression, JsValue handler)
        {
            if (_disposed) return null;
            if (!CronExpression.TryParse(expression, out var cron))
            {
                _logger.LogWarning("[JellyFrame] Scheduler [{Mod}] invalid cron '{Expr}'", _modId, expression);
                return null;
            }

            var id = NextId();
            ScheduleNextCron(id, cron, handler);
            _logger.LogDebug("[JellyFrame] Scheduler [{Mod}] cron task '{Id}' registered ('{Expr}')",
                _modId, id, expression);
            return id;
        }

        public void Cancel(string id)
        {
            if (_tasks.TryRemove(id, out var t))
            {
                t.Dispose();
                _logger.LogDebug("[JellyFrame] Scheduler [{Mod}] task '{Id}' cancelled", _modId, id);
            }
        }

        public void CancelAll()
        {
            foreach (var kv in _tasks)
            {
                _tasks.TryRemove(kv.Key, out _);
                kv.Value.Dispose();
            }
            _logger.LogDebug("[JellyFrame] Scheduler [{Mod}] all tasks cancelled", _modId);
        }

        public int Count => _tasks.Count;

        private void ScheduleNextCron(string id, CronExpression cron, JsValue handler)
        {
            var next = cron.Next(DateTime.UtcNow);
            if (next == null) return;

            var delay = next.Value - DateTime.UtcNow;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            var timer = new Timer(_ =>
            {
                Invoke(id, handler);

                if (_tasks.ContainsKey(id))
                    ScheduleNextCron(id, cron, handler);
            }, null, delay, Timeout.InfiniteTimeSpan);

            if (_tasks.TryRemove(id, out var old)) old.Dispose();
            _tasks[id] = timer;
        }

        private void Invoke(string id, JsValue handler)
        {
            if (_disposed || _engine == null) return;
            if (!_tasks.ContainsKey(id)) return;

            try
            {
                _engine.Invoke(handler, JsValue.Undefined, Array.Empty<JsValue>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyFrame] Scheduler [{Mod}] task '{Id}' threw", _modId, id);
            }
        }

        private string NextId() => "task-" + Interlocked.Increment(ref _idCounter);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelAll();
        }

        private class CronExpression
        {
            private readonly HashSet<int> _minutes;
            private readonly HashSet<int> _hours;
            private readonly HashSet<int> _doms;
            private readonly HashSet<int> _months;
            private readonly HashSet<int> _dows;

            private CronExpression(int[] min, int[] hr, int[] dom, int[] mon, int[] dow)
            {
                _minutes = new HashSet<int>(min);
                _hours = new HashSet<int>(hr);
                _doms = new HashSet<int>(dom);
                _months = new HashSet<int>(mon);
                _dows = new HashSet<int>(dow);
            }

            public static bool TryParse(string expr, out CronExpression result)
            {
                result = null;
                if (string.IsNullOrWhiteSpace(expr)) return false;
                var parts = expr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5) return false;
                try
                {
                    result = new CronExpression(
                        ParseField(parts[0], 0, 59),
                        ParseField(parts[1], 0, 23),
                        ParseField(parts[2], 1, 31),
                        ParseField(parts[3], 1, 12),
                        ParseField(parts[4], 0, 6));
                    return true;
                }
                catch { return false; }
            }

            public DateTime? Next(DateTime after)
            {
                var t = after.AddSeconds(60 - after.Second).AddMilliseconds(-after.Millisecond);

                var limit = after.AddYears(4);
                while (t < limit)
                {
                    if (!_months.Contains(t.Month)) { t = t.AddMonths(1).Date.AddHours(0); continue; }
                    if (!_doms.Contains(t.Day) && !_dows.Contains((int)t.DayOfWeek)) { t = t.Date.AddDays(1); continue; }
                    if (!_hours.Contains(t.Hour)) { t = t.Date.AddHours(t.Hour + 1); continue; }
                    if (!_minutes.Contains(t.Minute)) { t = t.AddMinutes(1); continue; }
                    return t;
                }
                return null;
            }

            private static int[] ParseField(string field, int min, int max)
            {
                if (field == "*") return Range(min, max);

                if (field.Contains('/'))
                {
                    var p = field.Split('/');
                    int step = int.Parse(p[1]);
                    int start = p[0] == "*" ? min : int.Parse(p[0]);
                    var vals = new List<int>();
                    for (int i = start; i <= max; i += step) vals.Add(i);
                    return vals.ToArray();
                }

                if (field.Contains(','))
                    return Array.ConvertAll(field.Split(','), s => Clamp(int.Parse(s.Trim()), min, max));

                if (field.Contains('-'))
                {
                    var p = field.Split('-');
                    return Range(int.Parse(p[0]), int.Parse(p[1]));
                }

                return new[] { Clamp(int.Parse(field), min, max) };
            }

            private static int[] Range(int from, int to)
            {
                var r = new int[to - from + 1];
                for (int i = 0; i < r.Length; i++) r[i] = from + i;
                return r;
            }

            private static int Clamp(int v, int min, int max)
                => v < min ? min : v > max ? max : v;
        }
    }
}