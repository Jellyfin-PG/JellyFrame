using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    /// <summary>
    /// SQLite-backed database surface exposed to mods as <c>jf.db</c>.
    /// Requires the <c>db</c> permission.
    ///
    /// All mods share a single backing file at
    /// <c>{DataPath}/JellyFrame/jellyframe.db</c>. Tables are automatically
    /// namespaced with <c>{modId}__</c> so mods cannot see or query each
    /// other's tables. From the mod's perspective, it has a private database;
    /// the prefix is invisible.
    ///
    /// Three levels of API:
    ///
    ///   1. Table helpers — no SQL required, covers the 80% case:
    ///        jf.db.table('ratings').insert({ itemId: 'x', rating: 7.5 });
    ///        jf.db.table('ratings').find({ rating: 7.5 });
    ///
    ///   2. Query builder — structured where-clauses, still no raw SQL:
    ///        jf.db.query('ratings')
    ///             .where({ rating: { gt: 7 } })
    ///             .orderBy('rating', 'desc')
    ///             .limit(20)
    ///             .run();
    ///
    ///   3. Raw SQL escape hatch — for anything the helpers can't express:
    ///        jf.db.exec("CREATE INDEX ...", []);
    ///        jf.db.queryRaw("SELECT * FROM ratings WHERE ...", [param]);
    ///        jf.db.transaction(function() { ... });
    ///
    /// Raw SQL is validated so a mod can only reference its own tables —
    /// `ATTACH`, `DETACH`, dangerous `PRAGMA` and cross-mod references
    /// are rejected.
    /// </summary>
    public sealed class DbSurface : IDisposable
    {
        // One shared process-wide lock used only for the *first* open of the
        // database file, to avoid a race when multiple mods all start at once
        // and all try to set WAL mode simultaneously.
        private static readonly object _firstOpenLock = new();
        private static bool _walInitialized = false;
        private static string _sharedDbPath = null;

        private readonly string _modId;
        private readonly string _tablePrefix;
        private readonly ILogger _logger;
        private readonly SqliteConnection _conn;
        private readonly object _connLock = new();
        private Engine _engine;
        private bool _disposed;

        public void SetEngine(Engine engine) => _engine = engine;

        // Valid-identifier regex for table and column names (the mod-facing
        // unprefixed names). Letters, digits, underscores; must not start
        // with a digit. Prevents SQL injection via dynamic identifier paths.
        private static readonly Regex IdentRegex =
            new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        public DbSurface(string modId, IApplicationPaths paths, ILogger logger)
        {
            _modId = modId;
            _logger = logger;
            _tablePrefix = SanitizeModIdForPrefix(modId) + "__";

            var dir = Path.Combine(paths.DataPath, "JellyFrame");
            Directory.CreateDirectory(dir);
            var dbPath = Path.Combine(dir, "jellyframe.db");

            lock (_firstOpenLock)
            {
                _sharedDbPath = dbPath;

                _conn = new SqliteConnection("Data Source=" + dbPath);
                _conn.Open();

                // Per-connection settings (must be set on every open).
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA busy_timeout=5000;";
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_keys=ON;";
                    cmd.ExecuteNonQuery();
                }

                // Database-level settings: apply once per process. WAL mode
                // persists on the file but re-issuing the pragma is cheap and
                // makes the behavior deterministic if someone else toggled it.
                if (!_walInitialized)
                {
                    using (var cmd = _conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA journal_mode=WAL;";
                        // Must consume the singleton result, or the pragma
                        // is silently ignored.
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read()) { /* drain */ }
                        }
                    }
                    using (var cmd = _conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
                        cmd.ExecuteNonQuery();
                    }
                    _walInitialized = true;
                }
            }
        }

        /// <summary>
        /// Returns a <see cref="DbTable"/> handle for the given mod-visible
        /// table name. The table is lazily created on first write.
        /// </summary>
        public DbTable Table(string name)
        {
            RequireValidIdentifier(name, "table name");
            return new DbTable(this, name);
        }

        /// <summary>
        /// Lists the mod's own tables (without the internal prefix).
        /// </summary>
        public string[] Tables()
        {
            const string sql =
                "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE @p ESCAPE '\\'";
            var like = EscapeLike(_tablePrefix) + "%";
            var rows = QueryInternal(sql, new[] { new KeyValuePair<string, object>("@p", like) });
            var result = new List<string>(rows.Length);
            foreach (var r in rows)
            {
                if (r.TryGetValue("name", out var n) && n is string full && full.StartsWith(_tablePrefix))
                    result.Add(full.Substring(_tablePrefix.Length));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Returns true if a mod-visible table exists.
        /// </summary>
        public bool HasTable(string name)
        {
            RequireValidIdentifier(name, "table name");
            return TableExists(name);
        }

        /// <summary>
        /// Drops a mod-visible table. Returns true if the table existed.
        /// </summary>
        public bool DropTable(string name)
        {
            RequireValidIdentifier(name, "table name");
            if (!TableExists(name)) return false;
            ExecInternal("DROP TABLE " + QuoteIdent(InternalTableName(name)), null);
            return true;
        }

        /// <summary>
        /// Start a fluent query against a mod-visible table. Call
        /// <c>.run()</c> at the end to execute.
        /// </summary>
        public DbQuery Query(string tableName)
        {
            RequireValidIdentifier(tableName, "table name");
            return new DbQuery(this, tableName);
        }

        /// <summary>
        /// Execute a statement that returns no rows (DDL, INSERT, UPDATE,
        /// DELETE). Parameters are positional: use <c>?</c> in SQL, pass a
        /// plain JS array or a single value.
        /// </summary>
        public void Exec(string sql, object parameters = null)
        {
            ValidateRawSql(sql);
            ExecInternal(sql, BindParams(parameters));
        }

        /// <summary>
        /// Execute a write statement and return a summary.
        /// </summary>
        public object Run(string sql, object parameters = null)
        {
            ValidateRawSql(sql);
            return RunInternal(sql, BindParams(parameters));
        }

        /// <summary>
        /// Execute a SELECT and return all rows as plain objects
        /// (column name → value).
        /// </summary>
        public object[] QueryRaw(string sql, object parameters = null)
        {
            ValidateRawSql(sql);
            var rows = QueryInternal(sql, BindParams(parameters));
            return RowsToJsArray(rows);
        }

        /// <summary>
        /// Run a JavaScript function inside a transaction. Commits on success;
        /// rolls back if the function throws. The function's return value is
        /// returned from transaction().
        ///
        /// Uses BEGIN IMMEDIATE so the write lock is acquired up front rather
        /// than on first write (which avoids a class of BUSY errors even
        /// when busy_timeout is set).
        /// </summary>
        public object Transaction(JsValue fn)
        {
            if (fn == null || fn.IsNull() || fn.IsUndefined())
                throw new ArgumentException("transaction() requires a function.");
            if (_engine == null)
                throw new InvalidOperationException(
                    "[JellyFrame] DbSurface has no Jint engine bound; cannot invoke transaction body.");

            lock (_connLock)
            {
                using var tx = _conn.BeginTransaction(IsolationLevel.Serializable);
                try
                {
                    var jsResult = _engine.Invoke(fn, JsValue.Undefined, Array.Empty<JsValue>());
                    tx.Commit();
                    return jsResult.IsUndefined() || jsResult.IsNull()
                        ? null
                        : jsResult.ToObject();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }
            }
        }

        internal string InternalTableName(string modVisibleName) => _tablePrefix + modVisibleName;

        internal string TablePrefix => _tablePrefix;

        internal bool TableExists(string modVisibleName)
        {
            const string sql = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n";
            var full = InternalTableName(modVisibleName);
            var rows = QueryInternal(sql, new[] { new KeyValuePair<string, object>("@n", full) });
            return rows.Length > 0;
        }

        internal string[] TableColumns(string modVisibleName)
        {
            var full = InternalTableName(modVisibleName);
            // table_info takes a table name directly; can't be parameterized.
            // Use a quoted identifier built from our own validated prefix.
            var sql = "PRAGMA table_info(" + QuoteIdent(full) + ")";
            var rows = QueryInternal(sql, null);
            var cols = new List<string>(rows.Length);
            foreach (var r in rows)
                if (r.TryGetValue("name", out var n) && n is string s) cols.Add(s);
            return cols.ToArray();
        }

        internal void EnsureTable(string modVisibleName, IDictionary<string, object> sampleRow)
        {
            if (TableExists(modVisibleName))
            {
                // Add any new columns present on the sample row.
                var existing = new HashSet<string>(TableColumns(modVisibleName), StringComparer.OrdinalIgnoreCase);
                foreach (var kv in sampleRow)
                {
                    if (!IdentRegex.IsMatch(kv.Key))
                        throw new ArgumentException("Invalid column name: '" + kv.Key + "'");
                    if (existing.Contains(kv.Key)) continue;
                    var alter = "ALTER TABLE " + QuoteIdent(InternalTableName(modVisibleName))
                              + " ADD COLUMN " + QuoteIdent(kv.Key) + " " + InferSqliteType(kv.Value);
                    ExecInternal(alter, null);
                }
                return;
            }

            var sb = new StringBuilder();
            sb.Append("CREATE TABLE ").Append(QuoteIdent(InternalTableName(modVisibleName))).Append(" (");
            bool first = true;
            foreach (var kv in sampleRow)
            {
                if (!IdentRegex.IsMatch(kv.Key))
                    throw new ArgumentException("Invalid column name: '" + kv.Key + "'");
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(QuoteIdent(kv.Key)).Append(' ').Append(InferSqliteType(kv.Value));
            }
            sb.Append(")");
            ExecInternal(sb.ToString(), null);
        }

        internal Dictionary<string, object>[] QueryInternal(
            string sql, IList<KeyValuePair<string, object>> parameters)
        {
            lock (_connLock)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = sql;
                if (parameters != null)
                    foreach (var p in parameters)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);

                using var reader = cmd.ExecuteReader();
                var results = new List<Dictionary<string, object>>();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>(reader.FieldCount);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[reader.GetName(i)] = val;
                    }
                    results.Add(row);
                }
                return results.ToArray();
            }
        }

        internal int ExecInternal(string sql, IList<KeyValuePair<string, object>> parameters)
        {
            lock (_connLock)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = sql;
                if (parameters != null)
                    foreach (var p in parameters)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
                return cmd.ExecuteNonQuery();
            }
        }

        internal object RunInternal(string sql, IList<KeyValuePair<string, object>> parameters)
        {
            lock (_connLock)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = sql;
                if (parameters != null)
                    foreach (var p in parameters)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
                int rowsAffected = cmd.ExecuteNonQuery();

                long lastId = 0;
                using (var idCmd = _conn.CreateCommand())
                {
                    idCmd.CommandText = "SELECT last_insert_rowid()";
                    var o = idCmd.ExecuteScalar();
                    if (o != null && o != DBNull.Value)
                        lastId = Convert.ToInt64(o);
                }
                return new { rowsAffected, lastInsertId = lastId };
            }
        }

        // Quote a SQLite identifier by wrapping in double-quotes and escaping
        // any internal double-quotes. Safe because we've already validated
        // that the identifier passed RequireValidIdentifier or derives from
        // the mod-id prefix (which is sanitized in SanitizeModIdForPrefix).
        internal static string QuoteIdent(string ident)
            => "\"" + ident.Replace("\"", "\"\"") + "\"";

        // Convert a Dictionary<string,object> row to an ExpandoObject so the
        // mod sees a proper JS object (dot-access, bracket-access, JSON.stringify
        // all work). Raw Dictionary<string,object> passed to JsValue.FromObject
        // only gets bracket-access and JSON.stringify is broken in Jint.
        internal static ExpandoObject RowToJsObject(Dictionary<string, object> row)
        {
            var exp = new ExpandoObject();
            var expDict = (IDictionary<string, object>)exp;
            foreach (var kv in row) expDict[kv.Key] = kv.Value;
            return exp;
        }

        internal static object[] RowsToJsArray(Dictionary<string, object>[] rows)
        {
            var result = new object[rows.Length];
            for (int i = 0; i < rows.Length; i++) result[i] = RowToJsObject(rows[i]);
            return result;
        }

        private static string EscapeLike(string s)
            => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        // ────────────────────────────────────────────────────────────────
        // Parameter binding: accept IDictionary (C#), IList (JS array),
        // ObjectInstance (JS object), or a single scalar
        // ────────────────────────────────────────────────────────────────

        internal static IList<KeyValuePair<string, object>> BindParams(object parameters)
        {
            if (parameters == null) return null;

            // JS array → positional params named @p0, @p1, ...
            if (parameters is System.Collections.IList list && !(parameters is string))
            {
                var result = new List<KeyValuePair<string, object>>(list.Count);
                for (int i = 0; i < list.Count; i++)
                    result.Add(new KeyValuePair<string, object>("@p" + i, CoerceJsValue(list[i])));
                return result;
            }

            // JS object or C# dict → named params
            if (parameters is IDictionary<string, object> dict)
            {
                var result = new List<KeyValuePair<string, object>>(dict.Count);
                foreach (var kv in dict)
                {
                    var name = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                    result.Add(new KeyValuePair<string, object>(name, CoerceJsValue(kv.Value)));
                }
                return result;
            }

            if (parameters is Jint.Native.Object.ObjectInstance jsObj)
            {
                var result = new List<KeyValuePair<string, object>>();
                foreach (var prop in jsObj.GetOwnProperties())
                {
                    var key = prop.Key.ToString();
                    var name = key.StartsWith("@") ? key : "@" + key;
                    result.Add(new KeyValuePair<string, object>(name, CoerceJsValue(prop.Value.Value?.ToObject())));
                }
                return result;
            }

            // Single scalar → single @p0
            return new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("@p0", CoerceJsValue(parameters))
            };
        }

        // Translate a JS/CLR value into something SQLite can bind. SQLite
        // natively handles string/long/double/byte[]/bool/null; anything
        // else falls back to ToString for safety.
        internal static object CoerceJsValue(object v)
        {
            if (v == null) return DBNull.Value;
            if (v is string) return v;
            if (v is bool b) return b ? 1L : 0L;
            if (v is byte[]) return v;
            if (v is sbyte or short or int or long or byte or ushort or uint or ulong)
                return Convert.ToInt64(v);
            if (v is float or double or decimal) return Convert.ToDouble(v);
            if (v is DateTime dt) return dt.ToString("o");
            return v.ToString();
        }

        internal static string InferSqliteType(object v)
        {
            if (v == null) return "TEXT";
            if (v is bool) return "INTEGER";
            if (v is byte[]) return "BLOB";
            if (v is sbyte or short or int or long or byte or ushort or uint or ulong)
                return "INTEGER";
            if (v is float or double or decimal) return "REAL";
            return "TEXT";
        }

        private static readonly string[] BannedKeywords = new[]
        {
            "ATTACH",
            "DETACH",
        };

        // Pragmas we allow mods to issue. Everything else is rejected.
        private static readonly HashSet<string> AllowedPragmas =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "table_info", "index_list", "index_info", "foreign_key_list",
                "user_version"
            };

        internal void ValidateRawSql(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL must not be empty.");

            // Strip line/block comments and string/identifier literals so we
            // check keywords against real tokens only, not strings like
            // 'ATTACH' inside quotes.
            var stripped = StripCommentsAndLiterals(sql).ToUpperInvariant();

            foreach (var kw in BannedKeywords)
            {
                if (Regex.IsMatch(stripped, "\\b" + kw + "\\b"))
                    throw new InvalidOperationException(
                        "[JellyFrame] mod '" + _modId + "' tried to use forbidden SQL keyword '" + kw + "'");
            }

            // PRAGMA: extract the pragma name and whitelist-check.
            var pragmaMatch = Regex.Match(stripped, "\\bPRAGMA\\s+([A-Z_][A-Z0-9_]*)", RegexOptions.IgnoreCase);
            if (pragmaMatch.Success)
            {
                var pragmaName = pragmaMatch.Groups[1].Value;
                if (!AllowedPragmas.Contains(pragmaName))
                    throw new InvalidOperationException(
                        "[JellyFrame] mod '" + _modId + "' tried to issue restricted PRAGMA '" + pragmaName + "'");
            }

            // Enforce table-name prefix: scan identifier-like tokens that
            // look like they might be table references (after FROM / JOIN /
            // INTO / UPDATE / TABLE) and reject anything that doesn't start
            // with this mod's prefix.
            //
            // We deliberately scan the comment/literal-stripped text so that
            // a string like 'other-mod__ratings' inside a VALUES clause
            // doesn't trip this.
            var strippedOriginalCase = StripCommentsAndLiterals(sql);
            var tableRefs = Regex.Matches(
                strippedOriginalCase,
                "\\b(?:FROM|JOIN|INTO|UPDATE|TABLE)\\s+([A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.IgnoreCase);
            foreach (Match m in tableRefs)
            {
                var name = m.Groups[1].Value;
                // Allow sqlite_master for introspection, but only read-only
                // contexts — which is already enforced by ValidateRawSql
                // being orthogonal to whether the caller used Exec/Run vs
                // QueryRaw. We accept sqlite_master here; raw-writes against
                // it would still fail at SQLite's own permission layer.
                if (name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!name.StartsWith(_tablePrefix, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "[JellyFrame] mod '" + _modId + "' tried to reference table '" + name
                        + "' which does not belong to it. Use jf.db.table('" + name
                        + "') — the mod prefix is applied automatically.");
            }
        }

        // Replace SQL string literals, identifier literals, line comments
        // and block comments with spaces of the same length. Keeps offsets
        // stable but makes keyword scanning safe from "false hits" inside
        // user data.
        private static string StripCommentsAndLiterals(string sql)
        {
            var sb = new StringBuilder(sql.Length);
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];

                // Line comment: -- ... \n
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    sb.Append("  ");
                    i += 2;
                    while (i < sql.Length && sql[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }

                // Block comment: /* ... */
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    sb.Append("  ");
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                    {
                        sb.Append(' ');
                        i++;
                    }
                    if (i + 1 < sql.Length) { sb.Append("  "); i += 2; }
                    continue;
                }

                // String literal: '...''...' (SQL '' escape)
                if (c == '\'')
                {
                    sb.Append(' ');
                    i++;
                    while (i < sql.Length)
                    {
                        if (sql[i] == '\'')
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == '\'')
                            {
                                sb.Append("  ");
                                i += 2;
                                continue;
                            }
                            sb.Append(' ');
                            i++;
                            break;
                        }
                        sb.Append(' ');
                        i++;
                    }
                    continue;
                }

                // Identifier literal: "..." (double-quoted) or [...] or `...`
                if (c == '"' || c == '[' || c == '`')
                {
                    char closer = c == '[' ? ']' : c;
                    sb.Append(' ');
                    i++;
                    while (i < sql.Length && sql[i] != closer) { sb.Append(' '); i++; }
                    if (i < sql.Length) { sb.Append(' '); i++; }
                    continue;
                }

                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static void RequireValidIdentifier(string s, string kind)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new ArgumentException(kind + " must not be empty.");
            if (!IdentRegex.IsMatch(s))
                throw new ArgumentException(
                    "Invalid " + kind + ": '" + s + "'. Only letters, digits and underscores; must not start with a digit.");
        }

        // ModIds allow hyphens in JellyFrame; SQL identifiers don't like
        // them in unquoted contexts. Map '-' to '_' so the prefix is safe
        // to embed. Since the result still goes through QuoteIdent in
        // dynamic-name contexts this is belt-and-suspenders.
        private static string SanitizeModIdForPrefix(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return "unknown";
            var sb = new StringBuilder(modId.Length);
            foreach (var c in modId)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                lock (_connLock)
                {
                    _conn?.Close();
                    _conn?.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[JellyFrame] Error disposing db connection for mod '{Id}'", _modId);
            }
        }
    }

    public sealed class DbTable
    {
        private readonly DbSurface _db;
        private readonly string _name; // mod-visible, unprefixed

        internal DbTable(DbSurface db, string name) { _db = db; _name = name; }

        public string Name => _name;

        /// <summary>Insert one row. Auto-creates the table on first call.</summary>
        public object Insert(object row)
        {
            var dict = ToDict(row);
            if (dict.Count == 0)
                throw new ArgumentException("Row has no fields.");
            _db.EnsureTable(_name, dict);

            var cols = new List<string>();
            var placeholders = new List<string>();
            var ps = new List<KeyValuePair<string, object>>();
            int i = 0;
            foreach (var kv in dict)
            {
                cols.Add(DbSurface.QuoteIdent(kv.Key));
                placeholders.Add("@v" + i);
                ps.Add(new KeyValuePair<string, object>("@v" + i, DbSurface.CoerceJsValue(kv.Value)));
                i++;
            }
            var sql = "INSERT INTO " + DbSurface.QuoteIdent(_db.InternalTableName(_name))
                    + " (" + string.Join(", ", cols) + ") VALUES (" + string.Join(", ", placeholders) + ")";
            return _db.RunInternal(sql, ps);
        }

        /// <summary>
        /// Insert-or-replace. <c>options.on</c> is an array of column names
        /// that identify a row; when a row with matching values exists,
        /// non-conflict columns are updated.
        /// </summary>
        public object Upsert(object row, object options = null)
        {
            var dict = ToDict(row);
            if (dict.Count == 0) throw new ArgumentException("Row has no fields.");
            _db.EnsureTable(_name, dict);

            var conflictCols = ExtractConflictCols(options);
            if (conflictCols.Count == 0)
            {
                // Fall back to primary-key-based REPLACE.
                var cols = new List<string>();
                var placeholders = new List<string>();
                var ps = new List<KeyValuePair<string, object>>();
                int i = 0;
                foreach (var kv in dict)
                {
                    cols.Add(DbSurface.QuoteIdent(kv.Key));
                    placeholders.Add("@v" + i);
                    ps.Add(new KeyValuePair<string, object>("@v" + i, DbSurface.CoerceJsValue(kv.Value)));
                    i++;
                }
                var sql = "INSERT OR REPLACE INTO " + DbSurface.QuoteIdent(_db.InternalTableName(_name))
                        + " (" + string.Join(", ", cols) + ") VALUES (" + string.Join(", ", placeholders) + ")";
                return _db.RunInternal(sql, ps);
            }

            // Ensure a unique index exists on the conflict columns so
            // ON CONFLICT can fire.
            EnsureUniqueIndex(conflictCols);

            var allCols = new List<string>();
            var allPh = new List<string>();
            var allPs = new List<KeyValuePair<string, object>>();
            int idx = 0;
            foreach (var kv in dict)
            {
                allCols.Add(DbSurface.QuoteIdent(kv.Key));
                allPh.Add("@v" + idx);
                allPs.Add(new KeyValuePair<string, object>("@v" + idx, DbSurface.CoerceJsValue(kv.Value)));
                idx++;
            }

            var updateAssigns = new List<string>();
            foreach (var kv in dict)
            {
                if (conflictCols.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) continue;
                updateAssigns.Add(
                    DbSurface.QuoteIdent(kv.Key) + "=excluded." + DbSurface.QuoteIdent(kv.Key));
            }

            var sb = new StringBuilder();
            sb.Append("INSERT INTO ").Append(DbSurface.QuoteIdent(_db.InternalTableName(_name)))
              .Append(" (").Append(string.Join(", ", allCols)).Append(")")
              .Append(" VALUES (").Append(string.Join(", ", allPh)).Append(")")
              .Append(" ON CONFLICT (")
              .Append(string.Join(", ", conflictCols.Select(DbSurface.QuoteIdent)))
              .Append(")");
            if (updateAssigns.Count > 0)
                sb.Append(" DO UPDATE SET ").Append(string.Join(", ", updateAssigns));
            else
                sb.Append(" DO NOTHING");

            return _db.RunInternal(sb.ToString(), allPs);
        }

        public object[] Find(object where = null)
        {
            if (!_db.TableExists(_name)) return Array.Empty<object>();
            var (sql, ps) = BuildSelect("*", where, null, null, null);
            var rows = _db.QueryInternal(sql, ps);
            return DbSurface.RowsToJsArray(rows);
        }

        public object FindOne(object where = null)
        {
            if (!_db.TableExists(_name)) return null;
            var (sql, ps) = BuildSelect("*", where, null, null, 1);
            var rows = _db.QueryInternal(sql, ps);
            return rows.Length > 0 ? (object)DbSurface.RowToJsObject(rows[0]) : null;
        }

        public object Update(object where, object patch)
        {
            if (!_db.TableExists(_name))
                return new { rowsAffected = 0, lastInsertId = 0L };
            var patchDict = ToDict(patch);
            if (patchDict.Count == 0) throw new ArgumentException("Patch has no fields.");

            var setClauses = new List<string>();
            var ps = new List<KeyValuePair<string, object>>();
            int i = 0;
            foreach (var kv in patchDict)
            {
                setClauses.Add(DbSurface.QuoteIdent(kv.Key) + "=@s" + i);
                ps.Add(new KeyValuePair<string, object>("@s" + i, DbSurface.CoerceJsValue(kv.Value)));
                i++;
            }

            var (whereSql, wherePs) = BuildWhere(where, ref i);
            foreach (var p in wherePs) ps.Add(p);

            var sql = "UPDATE " + DbSurface.QuoteIdent(_db.InternalTableName(_name))
                    + " SET " + string.Join(", ", setClauses)
                    + whereSql;
            return _db.RunInternal(sql, ps);
        }

        public object Delete(object where = null)
        {
            if (!_db.TableExists(_name))
                return new { rowsAffected = 0, lastInsertId = 0L };
            int i = 0;
            var (whereSql, ps) = BuildWhere(where, ref i);
            var sql = "DELETE FROM " + DbSurface.QuoteIdent(_db.InternalTableName(_name)) + whereSql;
            return _db.RunInternal(sql, ps);
        }

        public long Count(object where = null)
        {
            if (!_db.TableExists(_name)) return 0;
            int i = 0;
            var (whereSql, ps) = BuildWhere(where, ref i);
            var sql = "SELECT COUNT(*) AS c FROM " + DbSurface.QuoteIdent(_db.InternalTableName(_name)) + whereSql;
            var rows = _db.QueryInternal(sql, ps);
            if (rows.Length == 0) return 0;
            var c = rows[0]["c"];
            return c == null ? 0 : Convert.ToInt64(c);
        }

        public object[] All() => Find(null);

        public bool Drop() => _db.DropTable(_name);

        public string[] Columns()
            => _db.TableExists(_name) ? _db.TableColumns(_name) : Array.Empty<string>();

        internal (string sql, List<KeyValuePair<string, object>> ps) BuildSelect(
            string selectList, object where, string orderByCol, string orderDir, int? limit)
        {
            int i = 0;
            var (whereSql, ps) = BuildWhere(where, ref i);
            var sb = new StringBuilder();
            sb.Append("SELECT ").Append(selectList)
              .Append(" FROM ").Append(DbSurface.QuoteIdent(_db.InternalTableName(_name)))
              .Append(whereSql);
            if (!string.IsNullOrEmpty(orderByCol))
            {
                sb.Append(" ORDER BY ").Append(DbSurface.QuoteIdent(orderByCol));
                if (string.Equals(orderDir, "desc", StringComparison.OrdinalIgnoreCase))
                    sb.Append(" DESC");
                else
                    sb.Append(" ASC");
            }
            if (limit.HasValue)
                sb.Append(" LIMIT ").Append(limit.Value);
            return (sb.ToString(), ps);
        }

        internal (string sql, List<KeyValuePair<string, object>> ps) BuildWhere(
            object where, ref int paramCounter)
        {
            var ps = new List<KeyValuePair<string, object>>();
            if (where == null) return ("", ps);

            var whereDict = ToDictOrNull(where);
            if (whereDict == null || whereDict.Count == 0) return ("", ps);

            var clauses = new List<string>();
            foreach (var kv in whereDict)
            {
                var col = kv.Key;
                if (!System.Text.RegularExpressions.Regex.IsMatch(col, "^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new ArgumentException("Invalid column name in where: '" + col + "'");

                var qcol = DbSurface.QuoteIdent(col);

                // Operator-object form: { rating: { gt: 7 } }
                var opMap = ToDictOrNull(kv.Value);
                if (opMap != null && opMap.Count > 0 && LooksLikeOpMap(opMap))
                {
                    foreach (var op in opMap)
                    {
                        string opKey = op.Key.ToLowerInvariant();
                        switch (opKey)
                        {
                            case "eq":
                                clauses.Add(qcol + "=@w" + paramCounter);
                                ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(op.Value)));
                                paramCounter++;
                                break;
                            case "ne":
                                clauses.Add(qcol + "<>@w" + paramCounter);
                                ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(op.Value)));
                                paramCounter++;
                                break;
                            case "gt":
                                clauses.Add(qcol + ">@w" + paramCounter);
                                ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(op.Value)));
                                paramCounter++;
                                break;
                            case "gte":
                                clauses.Add(qcol + ">=@w" + paramCounter);
                                ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(op.Value)));
                                paramCounter++;
                                break;
                            case "lt":
                                clauses.Add(qcol + "<@w" + paramCounter);
                                ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(op.Value)));
                                paramCounter++;
                                break;
                            case "lte":
                                clauses.Add(qcol + "<=@w" + paramCounter);
                                ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(op.Value)));
                                paramCounter++;
                                break;
                            case "like":
                                clauses.Add(qcol + " LIKE @w" + paramCounter);
                                ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(op.Value)));
                                paramCounter++;
                                break;
                            case "in":
                                {
                                    var list = op.Value as System.Collections.IList;
                                    if (list == null || list.Count == 0)
                                    {
                                        clauses.Add("0"); // always-false
                                        break;
                                    }
                                    var phs = new List<string>(list.Count);
                                    foreach (var v in list)
                                    {
                                        phs.Add("@w" + paramCounter);
                                        ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(v)));
                                        paramCounter++;
                                    }
                                    clauses.Add(qcol + " IN (" + string.Join(", ", phs) + ")");
                                    break;
                                }
                            case "between":
                                {
                                    var list = op.Value as System.Collections.IList;
                                    if (list == null || list.Count != 2)
                                        throw new ArgumentException("'between' expects a 2-element array.");
                                    clauses.Add(qcol + " BETWEEN @w" + paramCounter + " AND @w" + (paramCounter + 1));
                                    ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(list[0])));
                                    ps.Add(new KeyValuePair<string, object>("@w" + (paramCounter + 1), DbSurface.CoerceJsValue(list[1])));
                                    paramCounter += 2;
                                    break;
                                }
                            case "isnull":
                                if (op.Value is bool bv && bv)
                                    clauses.Add(qcol + " IS NULL");
                                else
                                    clauses.Add(qcol + " IS NOT NULL");
                                break;
                            case "notnull":
                                clauses.Add(qcol + " IS NOT NULL");
                                break;
                            default:
                                throw new ArgumentException(
                                    "Unknown operator '" + op.Key + "' in where clause for column '" + col + "'");
                        }
                    }
                    continue;
                }

                // Plain equality form: { rating: 7.5 }
                if (kv.Value == null)
                {
                    clauses.Add(qcol + " IS NULL");
                }
                else
                {
                    clauses.Add(qcol + "=@w" + paramCounter);
                    ps.Add(new KeyValuePair<string, object>("@w" + paramCounter, DbSurface.CoerceJsValue(kv.Value)));
                    paramCounter++;
                }
            }

            if (clauses.Count == 0) return ("", ps);
            return (" WHERE " + string.Join(" AND ", clauses), ps);
        }

        private static bool LooksLikeOpMap(IDictionary<string, object> d)
        {
            // Heuristic: op-maps have keys that are all known op names.
            foreach (var k in d.Keys)
            {
                var lk = k.ToLowerInvariant();
                if (lk != "eq" && lk != "ne" && lk != "gt" && lk != "gte"
                    && lk != "lt" && lk != "lte" && lk != "like"
                    && lk != "in" && lk != "between"
                    && lk != "isnull" && lk != "notnull")
                    return false;
            }
            return true;
        }

        private void EnsureUniqueIndex(List<string> cols)
        {
            var idxName = "jf_uniq_" + _db.InternalTableName(_name) + "_"
                        + string.Join("_", cols);
            // Truncate overly-long names (SQLite accepts long but be tidy).
            if (idxName.Length > 200) idxName = idxName.Substring(0, 200);

            var sql = "CREATE UNIQUE INDEX IF NOT EXISTS " + DbSurface.QuoteIdent(idxName)
                    + " ON " + DbSurface.QuoteIdent(_db.InternalTableName(_name))
                    + " (" + string.Join(", ", cols.Select(DbSurface.QuoteIdent)) + ")";
            _db.ExecInternal(sql, null);
        }

        private static List<string> ExtractConflictCols(object options)
        {
            var result = new List<string>();
            if (options == null) return result;
            var dict = ToDictOrNull(options);
            if (dict == null) return result;
            if (!dict.TryGetValue("on", out var onVal) || onVal == null) return result;
            if (onVal is System.Collections.IList list)
            {
                foreach (var item in list)
                    if (item != null) result.Add(item.ToString());
            }
            else
            {
                result.Add(onVal.ToString());
            }
            foreach (var c in result)
                if (!System.Text.RegularExpressions.Regex.IsMatch(c, "^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new ArgumentException("Invalid conflict column: '" + c + "'");
            return result;
        }

        private static Dictionary<string, object> ToDict(object row)
        {
            var d = ToDictOrNull(row);
            if (d == null) throw new ArgumentException("Expected an object, got " + (row?.GetType().Name ?? "null"));
            return d;
        }

        internal static Dictionary<string, object> ToDictOrNull(object row)
        {
            if (row == null) return null;
            if (row is IDictionary<string, object> dict)
                return new Dictionary<string, object>(dict, StringComparer.OrdinalIgnoreCase);

            if (row is Jint.Native.Object.ObjectInstance jsObj)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in jsObj.GetOwnProperties())
                    result[prop.Key.ToString()] = prop.Value.Value?.ToObject();
                return result;
            }

            return null;
        }
    }

    public sealed class DbQuery
    {
        private readonly DbSurface _db;
        private readonly DbTable _table;
        private object _where;
        private string _orderCol;
        private string _orderDir = "asc";
        private int? _limit;
        private string _selectList = "*";

        internal DbQuery(DbSurface db, string tableName)
        {
            _db = db;
            _table = new DbTable(db, tableName);
        }

        public DbQuery Where(object where) { _where = where; return this; }
        public DbQuery OrderBy(string column, string direction = "asc")
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(column ?? "", "^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new ArgumentException("Invalid orderBy column: '" + column + "'");
            _orderCol = column;
            _orderDir = direction;
            return this;
        }
        public DbQuery Limit(int n) { _limit = n; return this; }
        public DbQuery Select(object columns)
        {
            // Accept array of column names or "*"
            if (columns is string s)
            {
                if (s == "*") { _selectList = "*"; return this; }
                // Single column name
                if (!System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new ArgumentException("Invalid select column: '" + s + "'");
                _selectList = DbSurface.QuoteIdent(s);
                return this;
            }
            if (columns is System.Collections.IList list)
            {
                var parts = new List<string>();
                foreach (var c in list)
                {
                    var cs = c?.ToString();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(cs ?? "", "^[A-Za-z_][A-Za-z0-9_]*$"))
                        throw new ArgumentException("Invalid select column: '" + cs + "'");
                    parts.Add(DbSurface.QuoteIdent(cs));
                }
                _selectList = string.Join(", ", parts);
                return this;
            }
            throw new ArgumentException("select() expects a column name string or array of names.");
        }

        public object[] Run()
        {
            if (!_db.TableExists(_table.Name)) return Array.Empty<object>();
            var (sql, ps) = _table.BuildSelect(_selectList, _where, _orderCol, _orderDir, _limit);
            var rows = _db.QueryInternal(sql, ps);
            return DbSurface.RowsToJsArray(rows);
        }

        public object First()
        {
            _limit = 1;
            var rows = Run();
            return rows.Length > 0 ? rows[0] : null;
        }

        public long Count() => _table.Count(_where);
    }
}