using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Legend.Storage;

internal sealed class InMemoryMailStore : IMailStore
{
    private readonly ConcurrentDictionary<string, MailRecord> _byId = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<MailRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        var list = _byId.Values
            .Where(m => string.Equals(m.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal))
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Clone())
            .ToList();
        return Task.FromResult<IReadOnlyList<MailRecord>>(list);
    }

    public Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        int n = _byId.Values.Count(m => string.Equals(m.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal));
        return Task.FromResult(n);
    }

    public Task<MailRecord?> GetByIdAsync(string mailId, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(mailId, out var m))
            return Task.FromResult<MailRecord?>(m.Clone());
        return Task.FromResult<MailRecord?>(null);
    }

    public Task<MailRecord> InsertAsync(MailRecord mail, CancellationToken ct = default)
    {
        var copy = mail.Clone();
        if (string.IsNullOrEmpty(copy.MailId))
            copy.MailId = Guid.NewGuid().ToString("N");
        if (copy.CreatedAt == default)
            copy.CreatedAt = DateTimeOffset.UtcNow;
        if (!_byId.TryAdd(copy.MailId, copy.Clone()))
            throw new InvalidOperationException("mail id collision");
        return Task.FromResult(copy.Clone());
    }

    public Task<(bool Ok, string? Error)> MarkReadAsync(string mailId, string ownerCharacterId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(mailId, out var m) ||
                !string.Equals(m.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal))
                return Task.FromResult<(bool, string?)>((false, "邮件不存在"));
            m.IsRead = true;
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    public Task<(bool Ok, string? Error, MailRecord? Mail)> TryClaimAsync(string mailId, string ownerCharacterId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(mailId, out var m) ||
                !string.Equals(m.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal))
                return Task.FromResult<(bool, string?, MailRecord?)>((false, "邮件不存在", null));
            if (m.Claimed)
                return Task.FromResult<(bool, string?, MailRecord?)>((false, "附件已领取", m.Clone()));
            if (!m.HasUnclaimedAttachment)
            {
                m.Claimed = true;
                m.IsRead = true;
                return Task.FromResult<(bool, string?, MailRecord?)>((true, null, m.Clone()));
            }
            var snapshot = m.Clone();
            m.Claimed = true;
            m.IsRead = true;
            return Task.FromResult<(bool, string?, MailRecord?)>((true, null, snapshot));
        }
    }

    public Task<(bool Ok, string? Error)> DeleteAsync(string mailId, string ownerCharacterId, bool allowUnclaimedAttachment, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(mailId, out var m) ||
                !string.Equals(m.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal))
                return Task.FromResult<(bool, string?)>((false, "邮件不存在"));
            if (!allowUnclaimedAttachment && m.HasUnclaimedAttachment)
                return Task.FromResult<(bool, string?)>((false, "有未领取附件，禁止删除"));
            _byId.TryRemove(mailId, out _);
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    public Task<int> EvictOldestClaimedEmptyAsync(string ownerCharacterId, int keepUnder, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var owned = _byId.Values
                .Where(m => string.Equals(m.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal))
                .OrderBy(m => m.CreatedAt)
                .ToList();
            int removed = 0;
            while (owned.Count >= keepUnder)
            {
                var victim = owned.FirstOrDefault(m => m.Claimed || !m.HasUnclaimedAttachment);
                if (victim is null)
                    break;
                _byId.TryRemove(victim.MailId, out _);
                owned.Remove(victim);
                removed++;
            }
            return Task.FromResult(removed);
        }
    }
}

internal sealed class SqliteMailStore : IMailStore
{
    private readonly string _cs;
    public SqliteMailStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS mails (
              mail_id TEXT PRIMARY KEY,
              owner_character_id TEXT NOT NULL,
              from_name TEXT NOT NULL DEFAULT '',
              subject TEXT NOT NULL DEFAULT '',
              body TEXT NOT NULL DEFAULT '',
              attach_gold INTEGER NOT NULL DEFAULT 0,
              attach_items_json TEXT NOT NULL DEFAULT '[]',
              is_read INTEGER NOT NULL DEFAULT 0,
              claimed INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_mails_owner ON mails(owner_character_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<MailRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at
            FROM mails WHERE owner_character_id = $o ORDER BY created_at DESC
            """;
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        var list = new List<MailRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ReadRow(r));
        return list;
    }

    public async Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM mails WHERE owner_character_id = $o";
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<MailRecord?> GetByIdAsync(string mailId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at
            FROM mails WHERE mail_id = $id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$id", mailId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadRow(r);
    }

    public async Task<MailRecord> InsertAsync(MailRecord mail, CancellationToken ct = default)
    {
        var copy = mail.Clone();
        if (string.IsNullOrEmpty(copy.MailId))
            copy.MailId = Guid.NewGuid().ToString("N");
        if (copy.CreatedAt == default)
            copy.CreatedAt = DateTimeOffset.UtcNow;
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mails (mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at)
            VALUES ($id, $o, $f, $s, $b, $g, $i, $r, $c, $t)
            """;
        Bind(cmd, copy);
        await cmd.ExecuteNonQueryAsync(ct);
        return copy;
    }

    public async Task<(bool Ok, string? Error)> MarkReadAsync(string mailId, string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE mails SET is_read = 1 WHERE mail_id = $id AND owner_character_id = $o";
        cmd.Parameters.AddWithValue("$id", mailId);
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        int n = await cmd.ExecuteNonQueryAsync(ct);
        return n > 0 ? (true, null) : (false, "邮件不存在");
    }

    public async Task<(bool Ok, string? Error, MailRecord? Mail)> TryClaimAsync(string mailId, string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var sel = conn.CreateCommand();
        sel.Transaction = (SqliteTransaction)tx;
        sel.CommandText = """
            SELECT mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at
            FROM mails WHERE mail_id = $id AND owner_character_id = $o LIMIT 1
            """;
        sel.Parameters.AddWithValue("$id", mailId);
        sel.Parameters.AddWithValue("$o", ownerCharacterId);
        MailRecord? mail;
        await using (var r = await sel.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
            {
                await tx.RollbackAsync(ct);
                return (false, "邮件不存在", null);
            }
            mail = ReadRow(r);
        }
        if (mail.Claimed)
        {
            await tx.RollbackAsync(ct);
            return (false, "附件已领取", mail);
        }
        await using var upd = conn.CreateCommand();
        upd.Transaction = (SqliteTransaction)tx;
        upd.CommandText = "UPDATE mails SET claimed = 1, is_read = 1 WHERE mail_id = $id AND owner_character_id = $o AND claimed = 0";
        upd.Parameters.AddWithValue("$id", mailId);
        upd.Parameters.AddWithValue("$o", ownerCharacterId);
        int n = await upd.ExecuteNonQueryAsync(ct);
        if (n == 0)
        {
            await tx.RollbackAsync(ct);
            return (false, "附件已领取", mail);
        }
        await tx.CommitAsync(ct);
        return (true, null, mail);
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string mailId, string ownerCharacterId, bool allowUnclaimedAttachment, CancellationToken ct = default)
    {
        var cur = await GetByIdAsync(mailId, ct);
        if (cur is null || !string.Equals(cur.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal))
            return (false, "邮件不存在");
        if (!allowUnclaimedAttachment && cur.HasUnclaimedAttachment)
            return (false, "有未领取附件，禁止删除");
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM mails WHERE mail_id = $id AND owner_character_id = $o";
        cmd.Parameters.AddWithValue("$id", mailId);
        cmd.Parameters.AddWithValue("$o", ownerCharacterId);
        int n = await cmd.ExecuteNonQueryAsync(ct);
        return n > 0 ? (true, null) : (false, "邮件不存在");
    }

    public async Task<int> EvictOldestClaimedEmptyAsync(string ownerCharacterId, int keepUnder, CancellationToken ct = default)
    {
        int removed = 0;
        while (await CountByOwnerAsync(ownerCharacterId, ct) >= keepUnder)
        {
            await using var conn = new SqliteConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT mail_id FROM mails
                WHERE owner_character_id = $o
                  AND (claimed = 1 OR (attach_gold <= 0 AND (attach_items_json = '[]' OR attach_items_json = '' OR attach_items_json IS NULL)))
                ORDER BY created_at ASC LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$o", ownerCharacterId);
            var idObj = await cmd.ExecuteScalarAsync(ct);
            if (idObj is null || idObj is DBNull)
                break;
            string id = Convert.ToString(idObj)!;
            await using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM mails WHERE mail_id = $id";
            del.Parameters.AddWithValue("$id", id);
            await del.ExecuteNonQueryAsync(ct);
            removed++;
        }
        return removed;
    }

    private static void Bind(SqliteCommand cmd, MailRecord m)
    {
        cmd.Parameters.AddWithValue("$id", m.MailId);
        cmd.Parameters.AddWithValue("$o", m.OwnerCharacterId);
        cmd.Parameters.AddWithValue("$f", m.FromName ?? "");
        cmd.Parameters.AddWithValue("$s", m.Subject ?? "");
        cmd.Parameters.AddWithValue("$b", m.Body ?? "");
        cmd.Parameters.AddWithValue("$g", m.AttachGold);
        cmd.Parameters.AddWithValue("$i", string.IsNullOrWhiteSpace(m.AttachItemsJson) ? "[]" : m.AttachItemsJson);
        cmd.Parameters.AddWithValue("$r", m.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("$c", m.Claimed ? 1 : 0);
        cmd.Parameters.AddWithValue("$t", m.CreatedAt.ToString("O"));
    }

    private static MailRecord ReadRow(SqliteDataReader r) => new()
    {
        MailId = r.GetString(0),
        OwnerCharacterId = r.GetString(1),
        FromName = r.GetString(2),
        Subject = r.GetString(3),
        Body = r.GetString(4),
        AttachGold = r.GetInt32(5),
        AttachItemsJson = r.GetString(6),
        IsRead = r.GetInt32(7) != 0,
        Claimed = r.GetInt32(8) != 0,
        CreatedAt = DateTimeOffset.Parse(r.GetString(9))
    };
}

internal sealed class PostgresMailStore : IMailStore
{
    private readonly string _cs;
    public PostgresMailStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS mails (
              mail_id VARCHAR(64) PRIMARY KEY,
              owner_character_id VARCHAR(64) NOT NULL,
              from_name VARCHAR(64) NOT NULL DEFAULT '',
              subject TEXT NOT NULL DEFAULT '',
              body TEXT NOT NULL DEFAULT '',
              attach_gold INTEGER NOT NULL DEFAULT 0,
              attach_items_json TEXT NOT NULL DEFAULT '[]',
              is_read BOOLEAN NOT NULL DEFAULT FALSE,
              claimed BOOLEAN NOT NULL DEFAULT FALSE,
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_mails_owner ON mails(owner_character_id);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<MailRecord>> ListByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at
            FROM mails WHERE owner_character_id = @o ORDER BY created_at DESC
            """, conn);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        var list = new List<MailRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ReadRow(r));
        return list;
    }

    public async Task<int> CountByOwnerAsync(string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT COUNT(1) FROM mails WHERE owner_character_id = @o", conn);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<MailRecord?> GetByIdAsync(string mailId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at
            FROM mails WHERE mail_id = @id LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", mailId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadRow(r);
    }

    public async Task<MailRecord> InsertAsync(MailRecord mail, CancellationToken ct = default)
    {
        var copy = mail.Clone();
        if (string.IsNullOrEmpty(copy.MailId))
            copy.MailId = Guid.NewGuid().ToString("N");
        if (copy.CreatedAt == default)
            copy.CreatedAt = DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO mails (mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at)
            VALUES (@id, @o, @f, @s, @b, @g, @i, @r, @c, @t)
            """, conn);
        cmd.Parameters.AddWithValue("id", copy.MailId);
        cmd.Parameters.AddWithValue("o", copy.OwnerCharacterId);
        cmd.Parameters.AddWithValue("f", copy.FromName ?? "");
        cmd.Parameters.AddWithValue("s", copy.Subject ?? "");
        cmd.Parameters.AddWithValue("b", copy.Body ?? "");
        cmd.Parameters.AddWithValue("g", copy.AttachGold);
        cmd.Parameters.AddWithValue("i", string.IsNullOrWhiteSpace(copy.AttachItemsJson) ? "[]" : copy.AttachItemsJson);
        cmd.Parameters.AddWithValue("r", copy.IsRead);
        cmd.Parameters.AddWithValue("c", copy.Claimed);
        cmd.Parameters.AddWithValue("t", copy.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
        return copy;
    }

    public async Task<(bool Ok, string? Error)> MarkReadAsync(string mailId, string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE mails SET is_read = TRUE WHERE mail_id = @id AND owner_character_id = @o", conn);
        cmd.Parameters.AddWithValue("id", mailId);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        int n = await cmd.ExecuteNonQueryAsync(ct);
        return n > 0 ? (true, null) : (false, "邮件不存在");
    }

    public async Task<(bool Ok, string? Error, MailRecord? Mail)> TryClaimAsync(string mailId, string ownerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var sel = new NpgsqlCommand("""
            SELECT mail_id, owner_character_id, from_name, subject, body, attach_gold, attach_items_json, is_read, claimed, created_at
            FROM mails WHERE mail_id = @id AND owner_character_id = @o LIMIT 1 FOR UPDATE
            """, conn, (NpgsqlTransaction)tx);
        sel.Parameters.AddWithValue("id", mailId);
        sel.Parameters.AddWithValue("o", ownerCharacterId);
        MailRecord? mail;
        await using (var r = await sel.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
            {
                await tx.RollbackAsync(ct);
                return (false, "邮件不存在", null);
            }
            mail = ReadRow(r);
        }
        if (mail.Claimed)
        {
            await tx.RollbackAsync(ct);
            return (false, "附件已领取", mail);
        }
        await using var upd = new NpgsqlCommand(
            "UPDATE mails SET claimed = TRUE, is_read = TRUE WHERE mail_id = @id AND owner_character_id = @o AND claimed = FALSE",
            conn, (NpgsqlTransaction)tx);
        upd.Parameters.AddWithValue("id", mailId);
        upd.Parameters.AddWithValue("o", ownerCharacterId);
        int n = await upd.ExecuteNonQueryAsync(ct);
        if (n == 0)
        {
            await tx.RollbackAsync(ct);
            return (false, "附件已领取", mail);
        }
        await tx.CommitAsync(ct);
        return (true, null, mail);
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string mailId, string ownerCharacterId, bool allowUnclaimedAttachment, CancellationToken ct = default)
    {
        var cur = await GetByIdAsync(mailId, ct);
        if (cur is null || !string.Equals(cur.OwnerCharacterId, ownerCharacterId, StringComparison.Ordinal))
            return (false, "邮件不存在");
        if (!allowUnclaimedAttachment && cur.HasUnclaimedAttachment)
            return (false, "有未领取附件，禁止删除");
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM mails WHERE mail_id = @id AND owner_character_id = @o", conn);
        cmd.Parameters.AddWithValue("id", mailId);
        cmd.Parameters.AddWithValue("o", ownerCharacterId);
        int n = await cmd.ExecuteNonQueryAsync(ct);
        return n > 0 ? (true, null) : (false, "邮件不存在");
    }

    public async Task<int> EvictOldestClaimedEmptyAsync(string ownerCharacterId, int keepUnder, CancellationToken ct = default)
    {
        int removed = 0;
        while (await CountByOwnerAsync(ownerCharacterId, ct) >= keepUnder)
        {
            await using var conn = new NpgsqlConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                DELETE FROM mails WHERE mail_id = (
                  SELECT mail_id FROM mails
                  WHERE owner_character_id = @o
                    AND (claimed = TRUE OR (attach_gold <= 0 AND (attach_items_json = '[]' OR attach_items_json = '' OR attach_items_json IS NULL)))
                  ORDER BY created_at ASC LIMIT 1
                )
                """, conn);
            cmd.Parameters.AddWithValue("o", ownerCharacterId);
            int n = await cmd.ExecuteNonQueryAsync(ct);
            if (n == 0) break;
            removed++;
        }
        return removed;
    }

    private static MailRecord ReadRow(NpgsqlDataReader r) => new()
    {
        MailId = r.GetString(0),
        OwnerCharacterId = r.GetString(1),
        FromName = r.GetString(2),
        Subject = r.GetString(3),
        Body = r.GetString(4),
        AttachGold = r.GetInt32(5),
        AttachItemsJson = r.GetString(6),
        IsRead = r.GetBoolean(7),
        Claimed = r.GetBoolean(8),
        CreatedAt = r.GetFieldValue<DateTimeOffset>(9)
    };
}
