using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    /// <summary>
    /// Inner implementation of native OS command access exposed to mods as <c>jf.os</c>.
    /// Requires the <c>os</c> permission.
    /// </summary>
    public class OsSurface
    {
        private readonly ILogger _logger;
        private readonly string _modId;

        private const int DefaultTimeoutMs = 30_000;
        private const int MaxTimeoutMs = 300_000; // 5 minutes

        public OsSurface(string modId, ILogger logger)
        {
            _modId = modId;
            _logger = logger;
        }

        /// <summary>
        /// Execute a native OS command synchronously.
        /// </summary>
        /// <param name="command">
        ///   The command to run. On Windows this is passed to <c>cmd.exe /c</c>;
        ///   on Linux/macOS it is passed to <c>/bin/sh -c</c>.
        /// </param>
        /// <param name="options">
        ///   Optional object with:
        ///   <list type="bullet">
        ///     <item><c>cwd</c> (string) – working directory</item>
        ///     <item><c>env</c> (object) – additional environment variables</item>
        ///     <item><c>timeoutMs</c> (number) – max milliseconds (default 30 000, max 300 000)</item>
        ///   </list>
        /// </param>
        /// <returns>
        ///   Object with <c>stdout</c>, <c>stderr</c>, <c>exitCode</c>, and <c>timedOut</c>.
        /// </returns>
        public ExecResult Exec(string command, object options = null)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("Command must not be empty.");

            var opts = ParseOptions(options);

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string shell = isWindows ? "cmd.exe" : "/bin/sh";
            string args = isWindows ? $"/c \"{command}\"" : $"-c \"{EscapeForShell(command)}\"";

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (!string.IsNullOrWhiteSpace(opts.Cwd))
                psi.WorkingDirectory = opts.Cwd;

            if (opts.Env != null)
                foreach (var kv in opts.Env)
                    psi.Environment[kv.Key] = kv.Value;

            _logger.LogInformation("[JellyFrame] Mod '{Id}' exec: {Cmd}", _modId, command);

            using var process = new Process { StartInfo = psi };
            process.Start();

            bool finished = process.WaitForExit(opts.TimeoutMs);

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();

            if (!finished)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("[JellyFrame] Mod '{Id}' exec timed out after {Ms}ms: {Cmd}",
                    _modId, opts.TimeoutMs, command);
                return new ExecResult
                {
                    Stdout = stdout,
                    Stderr = stderr,
                    ExitCode = -1,
                    TimedOut = true
                };
            }

            return new ExecResult
            {
                Stdout = stdout,
                Stderr = stderr,
                ExitCode = process.ExitCode,
                TimedOut = false
            };
        }

        /// <summary>Get the value of an environment variable, or null if not set.</summary>
        public string Env(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Variable name must not be empty.");
            return System.Environment.GetEnvironmentVariable(name);
        }

        /// <summary>
        /// Get all environment variables as a plain object.
        /// </summary>
        public object EnvAll()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
                result[entry.Key?.ToString() ?? ""] = entry.Value?.ToString() ?? "";
            return result;
        }

        /// <summary>
        /// Returns a string describing the current OS: <c>"windows"</c>, <c>"linux"</c>,
        /// or <c>"osx"</c>.
        /// </summary>
        public string Platform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx";
            return "linux";
        }

        /// <summary>
        /// Returns the OS description string from <see cref="RuntimeInformation.OSDescription"/>.
        /// </summary>
        public string OsDescription() => RuntimeInformation.OSDescription;

        /// <summary>Returns the machine's hostname.</summary>
        public string Hostname() => System.Net.Dns.GetHostName();

        /// <summary>Returns the number of logical CPU cores available to the process.</summary>
        public int CpuCount() => System.Environment.ProcessorCount;

        /// <summary>
        /// Returns approximate total and available physical memory in bytes,
        /// along with current process memory usage.
        /// </summary>
        public object MemoryInfo()
        {
            using var proc = Process.GetCurrentProcess();
            return new
            {
                processWorkingSetBytes = proc.WorkingSet64,
                processPrivateBytes = proc.PrivateMemorySize64,
                gcTotalMemoryBytes = GC.GetTotalMemory(forceFullCollection: false)
            };
        }

        private ExecOptions ParseOptions(object options)
        {
            var result = new ExecOptions { TimeoutMs = DefaultTimeoutMs };
            if (options == null) return result;

            IDictionary<string, object> opts = null;

            if (options is IDictionary<string, object> dOpts)
            {
                opts = dOpts;
            }
            else if (options is Jint.Native.Object.ObjectInstance jsOpts)
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsOpts.GetOwnProperties())
                    d[prop.Key.ToString()] = prop.Value.Value?.ToObject();
                opts = d;
            }

            if (opts == null) return result;

            if (opts.TryGetValue("cwd", out var cwd) && cwd != null)
                result.Cwd = cwd.ToString();

            if (opts.TryGetValue("timeoutMs", out var t) && t != null)
            {
                if (double.TryParse(t.ToString(), out double ms))
                    result.TimeoutMs = (int)Math.Min(Math.Max(ms, 1000), MaxTimeoutMs);
            }

            if (opts.TryGetValue("env", out var envObj) && envObj != null)
            {
                result.Env = new Dictionary<string, string>(StringComparer.Ordinal);
                if (envObj is IDictionary<string, object> envDict)
                    foreach (var kv in envDict)
                        result.Env[kv.Key] = kv.Value?.ToString() ?? "";
                else if (envObj is Jint.Native.Object.ObjectInstance jsEnv)
                    foreach (var prop in jsEnv.GetOwnProperties())
                        result.Env[prop.Key.ToString()] = prop.Value.Value?.ToString() ?? "";
            }

            return result;
        }

        private static string EscapeForShell(string cmd)
            => cmd.Replace("\"", "\\\"");

        public class ExecResult
        {
            public string Stdout { get; set; }
            public string Stderr { get; set; }
            public int ExitCode { get; set; }
            public bool TimedOut { get; set; }
        }

        private class ExecOptions
        {
            public string Cwd { get; set; }
            public int TimeoutMs { get; set; }
            public Dictionary<string, string> Env { get; set; }
        }
    }
}
