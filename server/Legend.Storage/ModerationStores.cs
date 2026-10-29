using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Legend.Storage;

internal sealed class InMemoryModerationStore : IModerationStore
{
    private readonly ConcurrentDictionary<string, ModerationRecord> _byAccount = new(StringComparer.Ordinal);

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<ModerationRecord?> GetByAccountIdAsync(string accountId, CancellationToken ct = default)
    {
        _byAccount.TryGetValue(accountId, out var rec);
        return Task.FromResult(Clone(rec));
    }

    public Task UpsertBanAsync(string accountId, bool banned, string reason, long expiresUnixMs, CancellationToken ct = default)
    {
        var rec = _byAccount.GetOrAdd(accountId, id => new ModerationRecord { AccountId = id });
        lock (rec)
        {
            rec.IsBanned = banned;
            rec.BanReason = reason ?? string.Empty;
            rec.BanExpiresUnixMs = banned ? expiresUnixMs : 0;
            rec.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task UpsertMuteAsync(string accountId, bool muted, string reason, long expiresUnixMs, CancellationToken ct = default)
    {
        var rec = _byAccount.GetOrAdd(accountId, id => new ModerationRecord { AccountId = id });
        lock (rec)
        {
            rec.IsMuted = muted;
            rec.MuteReason = reason ?? string.Empty;
            rec.MuteExpiresUnixMs = muted ? expiresUnixMs : 0;
            rec.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    private static ModerationRecord? Clone(ModerationRecord? src)
    {
        if (src is null) return null;
        lock (src)
        {
            return new ModerationRecord
            {
                AccountId = src.AccountId,
                IsBanned = src.IsBanned,
                BanReason = src.BanReason,
                BanExpiresUnixMs = src.BanExpiresUnixMs,
                IsMuted = src.IsMuted,
                MuteReason = src.MuteReason,
                MuteExpiresUnixMs = src.MuteExpiresUnixMs,
                UpdatedAt = src.UpdatedAt
            };
        }
    }
}

internal sealed class SqliteModerationStore : IModerationStore
{
    private readonly string _cs;
    public SqliteModerationStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS account_moderation (
              account_id TEXT PRIMARY KEY,
              is_banned INTEGER NOT NULL DEFAULT 0,
              ban_reason TEXT NOT NULL DEFAULT '',
              ban_expires_unix_ms INTEGER NOT NULL DEFAULT 0,
              is_muted INTEGER NOT NULL DEFAULT 0,
              mute_reason TEXT NOT NULL DEFAULT '',
              mute_expires_unix_ms INTEGER NOT NULL DEFAULT 0,
              updated_at TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ModerationRecord?> GetByAccountIdAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT account_id, is_banned, ban_reason, ban_expires_unix_ms,
                   is_muted, mute_reason, mute_expires_unix_ms, updated_at
            FROM account_moderation WHERE account_id=$id
            """;
        cmd.Parameters.AddWithValue("$id", accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ModerationRecord
        {
            AccountId = reader.GetString(0),
            IsBanned = reader.GetInt64(1) != 0,
            BanReason = reader.GetString(2),
            BanExpiresUnixMs = reader.GetInt64(3),
            IsMuted = reader.GetInt64(4) != 0,
            MuteReason = reader.GetString(5),
            MuteExpiresUnixMs = reader.GetInt64(6),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(7))
        };
    }

    public async Task UpsertBanAsync(string accountId, bool banned, string reason, long expiresUnixMs, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO account_moderation(account_id, is_banned, ban_reason, ban_expires_unix_ms,
              is_muted, mute_reason, mute_expires_unix_ms, updated_at)
            VALUES($id, $ban, $br, $be, 0, '', 0, $u)
            ON CONFLICT(account_id) DO UPDATE SET
              is_banned=excluded.is_banned,
              ban_reason=excluded.ban_reason,
              ban_expires_unix_ms=excluded.ban_expires_unix_ms,
              updated_at=excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.Parameters.AddWithValue("$ban", banned ? 1 : 0);
        cmd.Parameters.AddWithValue("$br", reason ?? "");
        cmd.Parameters.AddWithValue("$be", banned ? expiresUnixMs : 0L);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertMuteAsync(string accountId, bool muted, string reason, long expiresUnixMs, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO account_moderation(account_id, is_banned, ban_reason, ban_expires_unix_ms,
              is_muted, mute_reason, mute_expires_unix_ms, updated_at)
            VALUES($id, 0, '', 0, $mute, $mr, $me, $u)
            ON CONFLICT(account_id) DO UPDATE SET
              is_muted=excluded.is_muted,
              mute_reason=excluded.mute_reason,
              mute_expires_unix_ms=excluded.mute_expires_unix_ms,
              updated_at=excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.Parameters.AddWithValue("$mute", muted ? 1 : 0);
        cmd.Parameters.AddWithValue("$mr", reason ?? "");
        cmd.Parameters.AddWithValue("$me", muted ? expiresUnixMs : 0L);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

internal sealed class PostgresModerationStore : IModerationStore
{
    private readonly string _cs;
    public PostgresModerationStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS account_moderation (
              account_id TEXT PRIMARY KEY,
              is_banned BOOLEAN NOT NULL DEFAULT FALSE,
              ban_reason TEXT NOT NULL DEFAULT '',
              ban_expires_unix_ms BIGINT NOT NULL DEFAULT 0,
              is_muted BOOLEAN NOT NULL DEFAULT FALSE,
              mute_reason TEXT NOT NULL DEFAULT '',
              mute_expires_unix_ms BIGINT NOT NULL DEFAULT 0,
              updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ModerationRecord?> GetByAccountIdAsync(string accountId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT account_id, is_banned, ban_reason, ban_expires_unix_ms,
                   is_muted, mute_reason, mute_expires_unix_ms, updated_at
            FROM account_moderation WHERE account_id=@id
            """, conn);
        cmd.Parameters.AddWithValue("id", accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ModerationRecord
        {
            AccountId = reader.GetString(0),
            IsBanned = reader.GetBoolean(1),
            BanReason = reader.GetString(2),
            BanExpiresUnixMs = reader.GetInt64(3),
            IsMuted = reader.GetBoolean(4),
            MuteReason = reader.GetString(5),
            MuteExpiresUnixMs = reader.GetInt64(6),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(7)
        };
    }

    public async Task UpsertBanAsync(string accountId, bool banned, string reason, long expiresUnixMs, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO account_moderation(account_id, is_banned, ban_reason, ban_expires_unix_ms,
              is_muted, mute_reason, mute_expires_unix_ms, updated_at)
            VALUES(@id, @ban, @br, @be, FALSE, '', 0, NOW())
            ON CONFLICT(account_id) DO UPDATE SET
              is_banned=EXCLUDED.is_banned,
              ban_reason=EXCLUDED.ban_reason,
              ban_expires_unix_ms=EXCLUDED.ban_expires_unix_ms,
              updated_at=NOW()
            """, conn);
        cmd.Parameters.AddWithValue("id", accountId);
        cmd.Parameters.AddWithValue("ban", banned);
        cmd.Parameters.AddWithValue("br", reason ?? "");
        cmd.Parameters.AddWithValue("be", banned ? expiresUnixMs : 0L);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertMuteAsync(string accountId, bool muted, string reason, long expiresUnixMs, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO account_moderation(account_id, is_banned, ban_reason, ban_expires_unix_ms,
              is_muted, mute_reason, mute_expires_unix_ms, updated_at)
            VALUES(@id, FALSE, '', 0, @mute, @mr, @me, NOW())
            ON CONFLICT(account_id) DO UPDATE SET
              is_muted=EXCLUDED.is_muted,
              mute_reason=EXCLUDED.mute_reason,
              mute_expires_unix_ms=EXCLUDED.mute_expires_unix_ms,
              updated_at=NOW()
            """, conn);
        cmd.Parameters.AddWithValue("id", accountId);
        cmd.Parameters.AddWithValue("mute", muted);
        cmd.Parameters.AddWithValue("mr", reason ?? "");
        cmd.Parameters.AddWithValue("me", muted ? expiresUnixMs : 0L);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
