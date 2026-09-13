using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlekrythaeCore
{
    /// <summary>
    /// Meggy'nin taşınabilir veri katmanı.
    ///
    /// Bütün kullanıcı verisi .alek dosyasının yanındaki kökte yaşar:
    ///   Data/meggy.db
    ///   Games/&lt;macera&gt;/game.db
    ///   Games/&lt;macera&gt;/Media/...
    ///
    /// Bu sınıf AppData, Documents, temp veya kullanıcı profiline oyun verisi yazmaz.
    /// </summary>
    internal sealed class PortableGameStore
    {
        private static readonly Regex DocumentKeyPattern =
            new Regex(@"^[a-zA-Z0-9_.-]{1,80}$", RegexOptions.Compiled);

        private static readonly HashSet<string> AllowedModes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "alekrytha",
                "real_world",
                "other_realm"
            };

        private static readonly HashSet<string> ReservedWindowsNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL", "CLOCK$",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };

        private readonly string _rootFolder;
        private readonly string _dataFolder;
        private readonly string _gamesFolder;
        private readonly string _templatesFolder;
        private readonly string _registryPath;

        public PortableGameStore(string rootFolder)
        {
            _rootFolder = Path.GetFullPath(rootFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _dataFolder = Path.Combine(_rootFolder, "Data");
            _gamesFolder = Path.Combine(_rootFolder, "Games");
            _templatesFolder = Path.Combine(_rootFolder, "Templates");
            _registryPath = Path.Combine(_dataFolder, "meggy.db");
        }

        public bool TryHandleApi(string operation, JsonElement payload, out object result)
        {
            switch (operation)
            {
                case "db.registry.read":
                    result = ReadRegistry();
                    return true;

                case "db.registry.write":
                    result = WriteRegistry(payload);
                    return true;

                case "db.game.ensure":
                    result = EnsureGame(payload);
                    return true;

                case "db.game.read":
                    result = ReadGameDocument(payload);
                    return true;

                case "db.game.write":
                    result = WriteGameDocument(payload);
                    return true;

                case "db.game.hasDocument":
                    result = HasGameDocument(payload);
                    return true;

                case "db.game.delete":
                    result = DeleteGame(payload);
                    return true;

                case "db.game.rename":
                    result = RenameGame(payload);
                    return true;

                default:
                    result = new { ok = false, error = "unsupported_portable_db_operation" };
                    return false;
            }
        }

        private object ReadRegistry()
        {
            Directory.CreateDirectory(_dataFolder);
            using SqliteConnection connection = OpenDatabase(_registryPath);
            EnsureRegistrySchema(connection);

            string activeId = string.Empty;
            using (SqliteCommand state = connection.CreateCommand())
            {
                state.CommandText = "SELECT value FROM app_state WHERE key = 'active_adventure_id' LIMIT 1;";
                activeId = state.ExecuteScalar() as string ?? string.Empty;
            }

            var adventures = new List<object>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT id, name, folder, cover, mode, cover_policy, created_at, updated_at
                    FROM adventures
                    ORDER BY created_at COLLATE NOCASE, name COLLATE NOCASE;";

                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    adventures.Add(new
                    {
                        id = reader.GetString(0),
                        name = reader.GetString(1),
                        folder = reader.GetString(2),
                        cover = reader.GetString(3),
                        mode = reader.GetString(4),
                        coverPolicy = reader.GetString(5),
                        createdAt = reader.GetString(6),
                        updatedAt = reader.GetString(7)
                    });
                }
            }

            return new
            {
                ok = true,
                data = new
                {
                    meta = new
                    {
                        schemaVersion = 2,
                        app = "Alekrythae Adventure Registry",
                        storage = "portable-sqlite"
                    },
                    activeId,
                    adventures
                }
            };
        }

        private object WriteRegistry(JsonElement payload)
        {
            JsonElement registry = ReadObject(payload, "data");
            if (registry.ValueKind != JsonValueKind.Object)
                return new { ok = false, error = "invalid_registry" };

            JsonElement adventuresNode = registry.TryGetProperty("adventures", out JsonElement list)
                ? list
                : default;
            if (adventuresNode.ValueKind != JsonValueKind.Array)
                return new { ok = false, error = "invalid_adventure_list" };

            string activeId = ReadString(registry, "activeId");
            var rows = new List<AdventureRow>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonElement item in adventuresNode.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    return new { ok = false, error = "invalid_adventure_record" };

                string id = ReadString(item, "id").Trim();
                string name = ReadString(item, "name").Trim();
                string folder = ReadString(item, "folder").Trim();
                string cover = ReadString(item, "cover");
                string mode = NormalizeMode(ReadString(item, "mode"));
                string coverPolicy = ReadString(item, "coverPolicy");
                string createdAt = ReadString(item, "createdAt");
                string updatedAt = ReadString(item, "updatedAt");

                if (string.IsNullOrWhiteSpace(id) || id.Length > 160)
                    return new { ok = false, error = "invalid_adventure_id" };
                if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
                    return new { ok = false, error = "invalid_adventure_name" };
                if (!TryNormalizeFolder(folder, out string safeFolder))
                    return new { ok = false, error = "invalid_adventure_folder" };
                if (!ids.Add(id) || !folders.Add(safeFolder))
                    return new { ok = false, error = "duplicate_adventure" };

                string now = DateTimeOffset.UtcNow.ToString("O");
                rows.Add(new AdventureRow(
                    id,
                    name,
                    safeFolder,
                    cover ?? string.Empty,
                    mode,
                    string.IsNullOrWhiteSpace(coverPolicy) ? "none" : coverPolicy,
                    string.IsNullOrWhiteSpace(createdAt) ? now : createdAt,
                    string.IsNullOrWhiteSpace(updatedAt) ? now : updatedAt));
            }

            if (!string.IsNullOrEmpty(activeId) && !ids.Contains(activeId))
                activeId = rows.Count > 0 ? rows[0].Id : string.Empty;

            Directory.CreateDirectory(_dataFolder);
            using SqliteConnection connection = OpenDatabase(_registryPath);
            EnsureRegistrySchema(connection);
            using SqliteTransaction transaction = connection.BeginTransaction();

            using (SqliteCommand clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM adventures;";
                clear.ExecuteNonQuery();
            }

            foreach (AdventureRow row in rows)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO adventures
                        (id, name, folder, cover, mode, cover_policy, created_at, updated_at)
                    VALUES
                        ($id, $name, $folder, $cover, $mode, $coverPolicy, $createdAt, $updatedAt);";
                insert.Parameters.AddWithValue("$id", row.Id);
                insert.Parameters.AddWithValue("$name", row.Name);
                insert.Parameters.AddWithValue("$folder", row.Folder);
                insert.Parameters.AddWithValue("$cover", row.Cover);
                insert.Parameters.AddWithValue("$mode", row.Mode);
                insert.Parameters.AddWithValue("$coverPolicy", row.CoverPolicy);
                insert.Parameters.AddWithValue("$createdAt", row.CreatedAt);
                insert.Parameters.AddWithValue("$updatedAt", row.UpdatedAt);
                insert.ExecuteNonQuery();
            }

            using (SqliteCommand state = connection.CreateCommand())
            {
                state.Transaction = transaction;
                state.CommandText = @"
                    INSERT INTO app_state(key, value)
                    VALUES('active_adventure_id', $activeId)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                state.Parameters.AddWithValue("$activeId", activeId);
                state.ExecuteNonQuery();
            }

            transaction.Commit();
            return new { ok = true };
        }

        private object EnsureGame(JsonElement payload)
        {
            string folder = ReadString(payload, "folder");
            if (!TryResolveGameFolder(folder, out string safeFolder, out string gameFolder))
                return new { ok = false, error = "invalid_adventure_folder" };

            string mode = NormalizeMode(ReadString(payload, "mode"));
            Directory.CreateDirectory(gameFolder);
            EnsureMediaFolders(gameFolder);

            string gameDb = Path.Combine(gameFolder, "game.db");
            bool created = !File.Exists(gameDb);
            if (created && string.Equals(mode, "alekrytha", StringComparison.Ordinal))
            {
                string template = Path.Combine(_templatesFolder, "Alekrytha", "game.db");
                if (File.Exists(template))
                    File.Copy(template, gameDb, overwrite: false);
            }

            using SqliteConnection connection = OpenDatabase(gameDb);
            EnsureGameSchema(connection);
            SetGameMeta(connection, "mode", mode);
            SetGameMeta(connection, "folder", safeFolder);

            return new
            {
                ok = true,
                created,
                folder = safeFolder,
                path = $"Games/{safeFolder}/game.db"
            };
        }

        private object ReadGameDocument(JsonElement payload)
        {
            if (!TryGetGameRequest(payload, out string folder, out string key, out string gameDb, out object? error))
                return error!;
            if (!File.Exists(gameDb))
                return new { ok = true, exists = false };

            using SqliteConnection connection = OpenDatabase(gameDb);
            EnsureGameSchema(connection);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT json_text FROM documents WHERE document_key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", key);
            string? json = command.ExecuteScalar() as string;
            if (json == null)
                return new { ok = true, exists = false };

            using JsonDocument document = JsonDocument.Parse(json);
            return new
            {
                ok = true,
                exists = true,
                folder,
                key,
                data = document.RootElement.Clone()
            };
        }

        private object HasGameDocument(JsonElement payload)
        {
            if (!TryGetGameRequest(payload, out string folder, out string key, out string gameDb, out object? error))
                return error!;
            if (!File.Exists(gameDb))
                return new { ok = true, exists = false };

            using SqliteConnection connection = OpenDatabase(gameDb);
            EnsureGameSchema(connection);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM documents WHERE document_key = $key);";
            command.Parameters.AddWithValue("$key", key);
            bool exists = Convert.ToInt64(command.ExecuteScalar() ?? 0) == 1;
            return new { ok = true, exists, folder, key };
        }

        private object WriteGameDocument(JsonElement payload)
        {
            if (!TryGetGameRequest(payload, out string folder, out string key, out string gameDb, out object? error))
                return error!;
            if (!payload.TryGetProperty("data", out JsonElement data)
                || data.ValueKind == JsonValueKind.Undefined)
            {
                return new { ok = false, error = "missing_document_data" };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(gameDb)!);
            string json = data.GetRawText();
            using (JsonDocument.Parse(json)) { }

            using SqliteConnection connection = OpenDatabase(gameDb);
            EnsureGameSchema(connection);
            using SqliteTransaction transaction = connection.BeginTransaction();

            string? previous = null;
            using (SqliteCommand read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT json_text FROM documents WHERE document_key = $key LIMIT 1;";
                read.Parameters.AddWithValue("$key", key);
                previous = read.ExecuteScalar() as string;
            }

            string now = DateTimeOffset.UtcNow.ToString("O");
            if (previous != null && !string.Equals(previous, json, StringComparison.Ordinal))
            {
                using SqliteCommand history = connection.CreateCommand();
                history.Transaction = transaction;
                history.CommandText = @"
                    INSERT INTO document_history(document_key, json_text, saved_utc)
                    VALUES($key, $json, $savedUtc);";
                history.Parameters.AddWithValue("$key", key);
                history.Parameters.AddWithValue("$json", previous);
                history.Parameters.AddWithValue("$savedUtc", now);
                history.ExecuteNonQuery();
            }

            using (SqliteCommand write = connection.CreateCommand())
            {
                write.Transaction = transaction;
                write.CommandText = @"
                    INSERT INTO documents(document_key, json_text, schema_version, updated_utc)
                    VALUES($key, $json, 1, $updatedUtc)
                    ON CONFLICT(document_key) DO UPDATE SET
                        json_text = excluded.json_text,
                        schema_version = excluded.schema_version,
                        updated_utc = excluded.updated_utc;";
                write.Parameters.AddWithValue("$key", key);
                write.Parameters.AddWithValue("$json", json);
                write.Parameters.AddWithValue("$updatedUtc", now);
                write.ExecuteNonQuery();
            }

            using (SqliteCommand prune = connection.CreateCommand())
            {
                prune.Transaction = transaction;
                prune.CommandText = @"
                    DELETE FROM document_history
                    WHERE history_id IN (
                        SELECT history_id
                        FROM document_history
                        WHERE document_key = $key
                        ORDER BY history_id DESC
                        LIMIT -1 OFFSET 50
                    );";
                prune.Parameters.AddWithValue("$key", key);
                prune.ExecuteNonQuery();
            }

            transaction.Commit();
            return new { ok = true, folder, key };
        }

        private object DeleteGame(JsonElement payload)
        {
            string folder = ReadString(payload, "folder");
            if (!TryResolveGameFolder(folder, out _, out string gameFolder))
                return new { ok = false, error = "invalid_adventure_folder" };

            if (!Directory.Exists(gameFolder))
                return new { ok = true, deleted = false };

            Directory.Delete(gameFolder, recursive: true);
            return new { ok = true, deleted = true };
        }

        private object RenameGame(JsonElement payload)
        {
            string oldFolder = ReadString(payload, "oldFolder");
            string newFolder = ReadString(payload, "newFolder");
            if (!TryResolveGameFolder(oldFolder, out _, out string oldPath)
                || !TryResolveGameFolder(newFolder, out string safeNewFolder, out string newPath))
            {
                return new { ok = false, error = "invalid_adventure_folder" };
            }

            if (!Directory.Exists(oldPath))
                return new { ok = false, error = "not_found" };
            if (Directory.Exists(newPath) || File.Exists(newPath))
                return new { ok = false, error = "destination_exists" };

            Directory.Move(oldPath, newPath);
            string gameDb = Path.Combine(newPath, "game.db");
            if (File.Exists(gameDb))
            {
                using SqliteConnection connection = OpenDatabase(gameDb);
                EnsureGameSchema(connection);
                SetGameMeta(connection, "folder", safeNewFolder);
            }

            return new { ok = true, folder = safeNewFolder };
        }

        private bool TryGetGameRequest(
            JsonElement payload,
            out string folder,
            out string key,
            out string gameDb,
            out object? error)
        {
            folder = ReadString(payload, "folder");
            key = ReadString(payload, "key");
            gameDb = string.Empty;
            error = null;

            if (!DocumentKeyPattern.IsMatch(key))
            {
                error = new { ok = false, error = "invalid_document_key" };
                return false;
            }

            if (!TryResolveGameFolder(folder, out folder, out string gameFolder))
            {
                error = new { ok = false, error = "invalid_adventure_folder" };
                return false;
            }

            gameDb = Path.Combine(gameFolder, "game.db");
            return true;
        }

        private bool TryResolveGameFolder(
            string candidate,
            out string safeFolder,
            out string fullPath)
        {
            safeFolder = string.Empty;
            fullPath = string.Empty;
            if (!TryNormalizeFolder(candidate, out safeFolder))
                return false;

            Directory.CreateDirectory(_gamesFolder);
            string gamesRoot = Path.GetFullPath(_gamesFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Path.Combine(_gamesFolder, safeFolder));
            if (!resolved.StartsWith(gamesRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = resolved;
            return true;
        }

        private static bool TryNormalizeFolder(string candidate, out string folder)
        {
            folder = (candidate ?? string.Empty).Normalize(NormalizationForm.FormC).Trim()
                .TrimEnd('.', ' ');
            string deviceStem = folder.Split('.')[0];
            if (string.IsNullOrWhiteSpace(folder)
                || folder.Length > 120
                || folder == "."
                || folder == ".."
                || ReservedWindowsNames.Contains(deviceStem)
                || !string.Equals(folder, Path.GetFileName(folder), StringComparison.Ordinal)
                || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                folder = string.Empty;
                return false;
            }

            return true;
        }

        private static string NormalizeMode(string? mode)
        {
            string value = mode ?? string.Empty;
            return AllowedModes.Contains(value) ? value : "other_realm";
        }

        private static JsonElement ReadObject(JsonElement payload, string property)
        {
            if (payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(property, out JsonElement node))
            {
                return node;
            }

            return payload;
        }

        private static string ReadString(JsonElement payload, string property)
        {
            if (payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(property, out JsonElement node)
                && node.ValueKind == JsonValueKind.String)
            {
                return node.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        internal static SqliteConnection OpenDatabase(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };

            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
            ExecutePragma(connection, "PRAGMA journal_mode = WAL;");
            ExecutePragma(connection, "PRAGMA synchronous = FULL;");
            ExecutePragma(connection, "PRAGMA busy_timeout = 5000;");
            return connection;
        }

        private static void ExecutePragma(SqliteConnection connection, string sql)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteScalar();
        }

        internal static void EnsureRegistrySchema(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS adventures (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    folder TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    cover TEXT NOT NULL DEFAULT '',
                    mode TEXT NOT NULL CHECK(mode IN ('alekrytha','real_world','other_realm')),
                    cover_policy TEXT NOT NULL DEFAULT 'none',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS app_state (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );";
            command.ExecuteNonQuery();
        }

        internal static void EnsureGameSchema(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS documents (
                    document_key TEXT PRIMARY KEY,
                    json_text TEXT NOT NULL CHECK(json_valid(json_text)),
                    schema_version INTEGER NOT NULL DEFAULT 1,
                    updated_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS document_history (
                    history_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    document_key TEXT NOT NULL,
                    json_text TEXT NOT NULL CHECK(json_valid(json_text)),
                    saved_utc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_document_history_key_id
                    ON document_history(document_key, history_id DESC);

                CREATE TABLE IF NOT EXISTS game_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );";
            command.ExecuteNonQuery();
        }

        internal static void SetGameMeta(SqliteConnection connection, string key, string value)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO game_meta(key, value)
                VALUES($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        internal static void EnsureMediaFolders(string gameFolder)
        {
            string media = Path.Combine(gameFolder, "Media");
            string[] paths =
            {
                media,
                Path.Combine(media, "Adventure"),
                Path.Combine(media, "Characters"),
                Path.Combine(media, "Locations"),
                Path.Combine(media, "Lore"),
                Path.Combine(media, "Map"),
                Path.Combine(media, "Reverie"),
                Path.Combine(media, "Audio"),
                Path.Combine(media, "Music"),
                Path.Combine(media, "Ambiance"),
                Path.Combine(media, "Effects"),
                Path.Combine(media, "Backgrounds"),
                Path.Combine(media, "Events")
            };

            foreach (string path in paths)
                Directory.CreateDirectory(path);
        }

        private sealed class AdventureRow
        {
            public AdventureRow(
                string id,
                string name,
                string folder,
                string cover,
                string mode,
                string coverPolicy,
                string createdAt,
                string updatedAt)
            {
                Id = id;
                Name = name;
                Folder = folder;
                Cover = cover;
                Mode = mode;
                CoverPolicy = coverPolicy;
                CreatedAt = createdAt;
                UpdatedAt = updatedAt;
            }

            public string Id { get; }
            public string Name { get; }
            public string Folder { get; }
            public string Cover { get; }
            public string Mode { get; }
            public string CoverPolicy { get; }
            public string CreatedAt { get; }
            public string UpdatedAt { get; }
        }
    }
}
