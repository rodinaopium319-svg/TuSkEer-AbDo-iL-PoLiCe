using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TuSkEer.AbDoILPoLiCe.Models;

namespace TuSkEer.AbDoILPoLiCe.Storage;

public enum ImportConflictMode
{
    SkipDuplicates = 0,
    CreateCopy = 1,
    OverwriteByName = 2
}

public sealed class ScriptExportBundle
{
    public string Application { get; set; } = "TuSkEer AbDo iL PoLiCe";
    public int SchemaVersion { get; set; } = 1;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public List<ScriptItem> Scripts { get; set; } = new();
}

public sealed class SqliteRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string DatabasePath => _databasePath;

    public SqliteRepository(string? customDatabasePath = null)
    {
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TuSkEerAbDoILPoLiCe");

        Directory.CreateDirectory(appDataDir);
        _databasePath = customDatabasePath ?? Path.Combine(appDataDir, "tuskeer.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA temp_store=MEMORY;

                CREATE TABLE IF NOT EXISTS Scripts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    HotkeyVk INTEGER NOT NULL DEFAULT 0,
                    HotkeyModifiers INTEGER NOT NULL DEFAULT 0,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    Category TEXT NOT NULL DEFAULT 'General',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAtUtc TEXT NOT NULL,
                    ModifiedAtUtc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_Scripts_Name ON Scripts(Name);
                CREATE INDEX IF NOT EXISTS IX_Scripts_Hotkey ON Scripts(HotkeyVk, HotkeyModifiers);

                CREATE TABLE IF NOT EXISTS AppMetadata (
                    MetaKey TEXT PRIMARY KEY,
                    MetaValue TEXT NOT NULL
                );
                """;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed starter scripts strictly once on initial installation
            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT MetaValue FROM AppMetadata WHERE MetaKey = 'SeedCompleted';";
            var existing = await checkCmd.ExecuteScalarAsync();
            if (existing is null)
            {
                await SeedStarterScriptsAsync(conn);

                await using var markCmd = conn.CreateCommand();
                markCmd.CommandText = "INSERT OR REPLACE INTO AppMetadata (MetaKey, MetaValue) VALUES ('SeedCompleted', '1');";
                await markCmd.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<ScriptItem>> GetAllScriptsAsync()
    {
        var items = new List<ScriptItem>(64);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, Content, HotkeyVk, HotkeyModifiers, IsEnabled, Category, SortOrder, CreatedAtUtc, ModifiedAtUtc
            FROM Scripts
            ORDER BY SortOrder ASC, Id ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(ReadScriptRow(reader));
        }

        return items;
    }

    public async Task<long> UpsertScriptAsync(ScriptItem script)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var now = DateTime.UtcNow;
            if (script.Id <= 0)
            {
                script.CreatedAtUtc = now;
                script.ModifiedAtUtc = now;

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO Scripts (Name, Content, HotkeyVk, HotkeyModifiers, IsEnabled, Category, SortOrder, CreatedAtUtc, ModifiedAtUtc)
                    VALUES ($name, $content, $vk, $mods, $enabled, $category, $sort, $created, $modified);
                    SELECT last_insert_rowid();
                    """;
                BindScriptParameters(cmd, script);
                var result = await cmd.ExecuteScalarAsync();
                script.Id = Convert.ToInt64(result, CultureInfo.InvariantCulture);
                return script.Id;
            }
            else
            {
                script.ModifiedAtUtc = now;
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE Scripts
                    SET Name = $name,
                        Content = $content,
                        HotkeyVk = $vk,
                        HotkeyModifiers = $mods,
                        IsEnabled = $enabled,
                        Category = $category,
                        SortOrder = $sort,
                        ModifiedAtUtc = $modified
                    WHERE Id = $id;
                    """;
                cmd.Parameters.AddWithValue("$id", script.Id);
                BindScriptParameters(cmd, script);
                await cmd.ExecuteNonQueryAsync();
                return script.Id;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteScriptAsync(long scriptId)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Scripts WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", scriptId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MetaValue FROM AppMetadata WHERE MetaKey = 'AppSettings';";
        var raw = await cmd.ExecuteScalarAsync() as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions)?.Clone() ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO AppMetadata (MetaKey, MetaValue) VALUES ('AppSettings', $val);";
            cmd.Parameters.AddWithValue("$val", json);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ExportScriptsToJsonAsync(string filePath)
    {
        var scripts = await GetAllScriptsAsync();
        var bundle = new ScriptExportBundle
        {
            ExportedAtUtc = DateTime.UtcNow,
            Scripts = scripts
        };
        string json = JsonSerializer.Serialize(bundle, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<int> ImportScriptsFromJsonAsync(string filePath, ImportConflictMode conflictMode)
    {
        string json = await File.ReadAllTextAsync(filePath);
        List<ScriptItem>? imported = null;

        try
        {
            var bundle = JsonSerializer.Deserialize<ScriptExportBundle>(json, JsonOptions);
            if (bundle?.Scripts is { Count: > 0 })
            {
                imported = bundle.Scripts;
            }
        }
        catch
        {
            // Fall through to raw array check
        }

        if (imported is null)
        {
            imported = JsonSerializer.Deserialize<List<ScriptItem>>(json, JsonOptions);
        }

        if (imported is null || imported.Count == 0)
            return 0;

        var existing = await GetAllScriptsAsync();
        var byName = new Dictionary<string, ScriptItem>(StringComparer.OrdinalIgnoreCase);
        var usedHotkeys = new HashSet<(int vk, HotkeyModifiers mods)>();

        foreach (var item in existing)
        {
            byName.TryAdd(item.Name, item);
            if (!item.Hotkey.IsEmpty)
            {
                usedHotkeys.Add((item.Hotkey.VirtualKey, item.Hotkey.Modifiers));
            }
        }

        int importedCount = 0;
        foreach (var rawItem in imported)
        {
            if (string.IsNullOrWhiteSpace(rawItem.Name)) continue;

            var candidate = new ScriptItem
            {
                Name = rawItem.Name.Trim(),
                Content = rawItem.Content ?? string.Empty,
                Hotkey = rawItem.Hotkey?.Clone() ?? new HotkeyBinding(),
                IsEnabled = rawItem.IsEnabled,
                Category = string.IsNullOrWhiteSpace(rawItem.Category) ? "Imported" : rawItem.Category,
                SortOrder = rawItem.SortOrder
            };

            // Prevent importing conflicting hotkeys
            if (!candidate.Hotkey.IsEmpty && usedHotkeys.Contains((candidate.Hotkey.VirtualKey, candidate.Hotkey.Modifiers)))
            {
                candidate.Hotkey = new HotkeyBinding();
            }

            if (byName.TryGetValue(candidate.Name, out var existingMatch))
            {
                switch (conflictMode)
                {
                    case ImportConflictMode.SkipDuplicates:
                        continue;
                    case ImportConflictMode.OverwriteByName:
                        candidate.Id = existingMatch.Id;
                        break;
                    case ImportConflictMode.CreateCopy:
                        candidate.Id = 0;
                        candidate.Name = $"{candidate.Name} (Imported)";
                        break;
                }
            }
            else
            {
                candidate.Id = 0;
            }

            await UpsertScriptAsync(candidate);
            if (!candidate.Hotkey.IsEmpty)
            {
                usedHotkeys.Add((candidate.Hotkey.VirtualKey, candidate.Hotkey.Modifiers));
            }
            importedCount++;
        }

        return importedCount;
    }

    public async Task BackupDatabaseAsync(string destinationPath)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var source = new SqliteConnection(_connectionString);
            await source.OpenAsync();

            var destBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            };

            await using var destination = new SqliteConnection(destBuilder.ToString());
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RestoreDatabaseAsync(string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("Backup file was not found.", backupFilePath);

        await _writeLock.WaitAsync();
        try
        {
            SqliteConnection.ClearAllPools();

            var srcBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = backupFilePath,
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var source = new SqliteConnection(srcBuilder.ToString());
            await source.OpenAsync();

            await using var destination = new SqliteConnection(_connectionString);
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void BindScriptParameters(SqliteCommand cmd, ScriptItem script)
    {
        cmd.Parameters.AddWithValue("$name", script.Name);
        cmd.Parameters.AddWithValue("$content", script.Content);
        cmd.Parameters.AddWithValue("$vk", script.Hotkey.VirtualKey);
        cmd.Parameters.AddWithValue("$mods", (int)script.Hotkey.Modifiers);
        cmd.Parameters.AddWithValue("$enabled", script.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$category", script.Category);
        cmd.Parameters.AddWithValue("$sort", script.SortOrder);
        cmd.Parameters.AddWithValue("$created", script.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$modified", script.ModifiedAtUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static ScriptItem ReadScriptRow(SqliteDataReader reader)
    {
        return new ScriptItem
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            Content = reader.GetString(2),
            Hotkey = new HotkeyBinding(reader.GetInt32(3), (HotkeyModifiers)reader.GetInt32(4)),
            IsEnabled = reader.GetInt32(5) != 0,
            Category = reader.GetString(6),
            SortOrder = reader.GetInt32(7),
            CreatedAtUtc = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ModifiedAtUtc = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }

    private static async Task SeedStarterScriptsAsync(SqliteConnection conn)
    {
        var starters = new[]
        {
            new ScriptItem
            {
                Name = "Script VIP",
                Content = "TuSkEer AbDo iL PoLiCe ❤️ مرحبا بك في برنامج\nأسرع برنامج كتابة تلقائية على ويندوز\n🔥 مع أفضل أداء وتصميم عصري",
                Hotkey = new HotkeyBinding(0x70, HotkeyModifiers.None), // F1
                Category = "Featured",
                SortOrder = 1
            },
            new ScriptItem
            {
                Name = "Auto Chat",
                Content = "Welcome everyone to the channel!\nType Smart ... Be Faster with TuSkEer.\nEnjoy instant high-precision automation.",
                Hotkey = new HotkeyBinding(0x73, HotkeyModifiers.None), // F4
                Category = "Chat",
                SortOrder = 2
            },
            new ScriptItem
            {
                Name = "Arabic Greeting",
                Content = "السلام عليكم ورحمة الله وبركاته\nأهلاً وسهلاً بكم جميعاً\nنتمنى لكم وقتاً ممتعاً معنا ❤️🔥",
                Hotkey = new HotkeyBinding(0x74, HotkeyModifiers.None), // F5
                Category = "Arabic",
                SortOrder = 3
            },
            new ScriptItem
            {
                Name = "Priority Interrupt Demo",
                Content = "Immediate priority alert message!\nSwitching cleanly without losing your previous script position.",
                Hotkey = new HotkeyBinding(0x75, HotkeyModifiers.None), // F6
                Category = "Priority",
                SortOrder = 4
            }
        };

        foreach (var item in starters)
        {
            var now = DateTime.UtcNow;
            item.CreatedAtUtc = now;
            item.ModifiedAtUtc = now;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Scripts (Name, Content, HotkeyVk, HotkeyModifiers, IsEnabled, Category, SortOrder, CreatedAtUtc, ModifiedAtUtc)
                VALUES ($name, $content, $vk, $mods, $enabled, $category, $sort, $created, $modified);
                """;
            BindScriptParameters(cmd, item);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
