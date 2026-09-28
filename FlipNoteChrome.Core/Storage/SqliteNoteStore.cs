using System.IO;
using Microsoft.Data.Sqlite;

namespace FlipNoteChrome.Core.Storage;

public sealed class SqliteNoteStore : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _conn;

    public SqliteNoteStore(string? dbPath = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome");
        Directory.CreateDirectory(dir);
        _dbPath = dbPath ?? Path.Combine(dir, "notes.db");
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS notes(
              stable_key TEXT PRIMARY KEY,
              exe_path TEXT NOT NULL,
              class_name TEXT NOT NULL,
              normalized_title TEXT NOT NULL,
              title_hash TEXT NOT NULL,
              aliases TEXT NOT NULL DEFAULT '[]',
              markdown TEXT NOT NULL DEFAULT '',
              updated_utc TEXT NOT NULL,
              created_utc TEXT NOT NULL,
              pinned INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_notes_updated ON notes(updated_utc DESC);
            CREATE TABLE IF NOT EXISTS notes_history(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              stable_key TEXT NOT NULL,
              markdown TEXT NOT NULL,
              saved_utc TEXT NOT NULL,
              FOREIGN KEY(stable_key) REFERENCES notes(stable_key) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_history_key ON notes_history(stable_key, saved_utc DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public void SaveWithHistory(WindowIdentity.WindowIdentity id, string markdown)
    {
        var existing = Get(id.StableKey) ?? FindByIdentity(id);
        string prev = existing?.Markdown ?? "";
        if (prev != markdown && !string.IsNullOrWhiteSpace(prev))
        {
            using var h = _conn.CreateCommand();
            h.CommandText = "INSERT INTO notes_history(stable_key, markdown, saved_utc) VALUES($k,$md,$t)";
            h.Parameters.AddWithValue("$k", id.StableKey);
            h.Parameters.AddWithValue("$md", prev);
            h.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            h.ExecuteNonQuery();
            // keep only last 50
            using var prune = _conn.CreateCommand();
            prune.CommandText = "DELETE FROM notes_history WHERE stable_key=$k AND id NOT IN (SELECT id FROM notes_history WHERE stable_key=$k ORDER BY saved_utc DESC LIMIT 50)";
            prune.Parameters.AddWithValue("$k", id.StableKey);
            prune.ExecuteNonQuery();
        }
        var note = GetOrCreate(id);
        note.Markdown = markdown;
        Upsert(note);
    }

    private Note? FindByIdentity(WindowIdentity.WindowIdentity id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes WHERE lower(exe_path)=lower($exe) AND class_name=$cls ORDER BY updated_utc DESC";
        cmd.Parameters.AddWithValue("$exe", id.ExePath);
        cmd.Parameters.AddWithValue("$cls", id.ClassName);
        using var r = cmd.ExecuteReader();
        var candidates = new List<Note>();
        while (r.Read())
        {
            var note = Map(r);
            candidates.Add(note);
            try
            {
                var aliases = System.Text.Json.JsonSerializer.Deserialize<List<string>>(note.AliasesJson) ?? [];
                if (note.NormalizedTitle.Equals(id.NormalizedTitle, StringComparison.OrdinalIgnoreCase) || aliases.Contains(id.NormalizedTitle, StringComparer.OrdinalIgnoreCase)) return note;
            }
            catch { }
        }
        // If there is only one matching application window, a changed Chrome
        // title can safely continue using that note's existing stable key.
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public List<(int Id, string Markdown, DateTime SavedUtc)> GetHistory(string stableKey, int limit = 20)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, markdown, saved_utc FROM notes_history WHERE stable_key=$k ORDER BY saved_utc DESC LIMIT $lim";
        cmd.Parameters.AddWithValue("$k", stableKey);
        cmd.Parameters.AddWithValue("$lim", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<(int, string, DateTime)>();
        while (r.Read()) list.Add((r.GetInt32(0), r.GetString(1), DateTime.Parse(r.GetString(2))));
        return list;
    }

    public void ExportAll(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var n in GetAll())
        {
            var safe = string.Join("_", n.NormalizedTitle.Split(Path.GetInvalidFileNameChars()))[..Math.Min(40, n.NormalizedTitle.Length)];
            if (string.IsNullOrWhiteSpace(safe)) safe = n.StableKey[..8];
            File.WriteAllText(Path.Combine(folder, $"{safe}-{n.StableKey[..8]}.md"), n.Markdown);
        }
    }

    public Note? Get(string stableKey)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes WHERE stable_key=$k LIMIT 1";
        cmd.Parameters.AddWithValue("$k", stableKey);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return Map(r);
    }

    public Note GetOrCreate(WindowIdentity.WindowIdentity id)
    {
        var existing = Get(id.StableKey);
        if (existing != null)
        {
            // Update alias if title changed
            if (!existing.AliasesJson.Contains(id.NormalizedTitle) && !string.IsNullOrEmpty(id.NormalizedTitle))
            {
                var aliases = System.Text.Json.JsonSerializer.Deserialize<List<string>>(existing.AliasesJson) ?? [];
                if (!aliases.Contains(id.NormalizedTitle)) aliases.Add(id.NormalizedTitle);
                existing.AliasesJson = System.Text.Json.JsonSerializer.Serialize(aliases);
                existing.NormalizedTitle = id.NormalizedTitle;
                Upsert(existing);
            }
            return existing;
        }
        var n = new Note
        {
            StableKey = id.StableKey,
            ExePath = id.ExePath,
            ClassName = id.ClassName,
            NormalizedTitle = id.NormalizedTitle,
            TitleHash = id.TitleHash,
            Markdown = "",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            Pinned = false
        };
        Upsert(n);
        return n;
    }

    public void Upsert(Note note)
    {
        note.UpdatedUtc = DateTime.UtcNow;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notes(stable_key,exe_path,class_name,normalized_title,title_hash,aliases,markdown,updated_utc,created_utc,pinned)
            VALUES($k,$exe,$cls,$nt,$th,$al,$md,$upd,$crt,$pin)
            ON CONFLICT(stable_key) DO UPDATE SET
              exe_path=excluded.exe_path,
              class_name=excluded.class_name,
              normalized_title=excluded.normalized_title,
              title_hash=excluded.title_hash,
              aliases=excluded.aliases,
              markdown=excluded.markdown,
              updated_utc=excluded.updated_utc,
              pinned=excluded.pinned
            """;
        cmd.Parameters.AddWithValue("$k", note.StableKey);
        cmd.Parameters.AddWithValue("$exe", note.ExePath);
        cmd.Parameters.AddWithValue("$cls", note.ClassName);
        cmd.Parameters.AddWithValue("$nt", note.NormalizedTitle);
        cmd.Parameters.AddWithValue("$th", note.TitleHash);
        cmd.Parameters.AddWithValue("$al", note.AliasesJson);
        cmd.Parameters.AddWithValue("$md", note.Markdown);
        cmd.Parameters.AddWithValue("$upd", note.UpdatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$crt", note.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$pin", note.Pinned ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string stableKey)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM notes WHERE stable_key=$k";
        cmd.Parameters.AddWithValue("$k", stableKey);
        cmd.ExecuteNonQuery();
    }

    public List<Note> Search(string query, int limit = 50)
    {
        using var cmd = _conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(query))
        {
            cmd.CommandText = "SELECT * FROM notes ORDER BY updated_utc DESC LIMIT $lim";
            cmd.Parameters.AddWithValue("$lim", limit);
        }
        else
        {
            cmd.CommandText = "SELECT * FROM notes WHERE markdown LIKE $q OR normalized_title LIKE $q ORDER BY updated_utc DESC LIMIT $lim";
            cmd.Parameters.AddWithValue("$q", $"%{query}%");
            cmd.Parameters.AddWithValue("$lim", limit);
        }
        using var r = cmd.ExecuteReader();
        var list = new List<Note>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public List<Note> GetAll() => Search("", 1000);

    private static Note Map(SqliteDataReader r) => new()
    {
        StableKey = r.GetString(0),
        ExePath = r.GetString(1),
        ClassName = r.GetString(2),
        NormalizedTitle = r.GetString(3),
        TitleHash = r.GetString(4),
        AliasesJson = r.GetString(5),
        Markdown = r.GetString(6),
        UpdatedUtc = DateTime.Parse(r.GetString(7)),
        CreatedUtc = DateTime.Parse(r.GetString(8)),
        Pinned = r.GetInt64(9) != 0
    };

    public string DbPath => _dbPath;
    public void Dispose() => _conn.Dispose();
}
