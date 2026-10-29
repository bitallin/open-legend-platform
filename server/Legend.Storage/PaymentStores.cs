using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Legend.Storage;

internal sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<string, PaymentOrderRecord> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _txnIndex = new(StringComparer.Ordinal);

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<PaymentOrderRecord> InsertAsync(PaymentOrderRecord order, CancellationToken ct = default)
    {
        var c = order.Clone();
        if (!_byId.TryAdd(c.OrderId, c))
            throw new InvalidOperationException("order id collision");
        return Task.FromResult(c.Clone());
    }

    public Task<PaymentOrderRecord?> GetByIdAsync(string orderId, CancellationToken ct = default)
    {
        _byId.TryGetValue(orderId, out var o);
        return Task.FromResult(o?.Clone());
    }

    public Task<PaymentOrderRecord?> GetByProviderTxnAsync(string provider, string providerTxnId, CancellationToken ct = default)
    {
        string key = $"{provider}|{providerTxnId}";
        if (_txnIndex.TryGetValue(key, out var id) && _byId.TryGetValue(id, out var o))
            return Task.FromResult<PaymentOrderRecord?>(o.Clone());
        return Task.FromResult<PaymentOrderRecord?>(null);
    }

    public Task<(bool Ok, string? Error, PaymentOrderRecord? Order)> TryMarkPaidAsync(string orderId, string providerTxnId, CancellationToken ct = default)
    {
        if (!_byId.TryGetValue(orderId, out var o))
            return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((false, "order_not_found", null));

        lock (o)
        {
            if (string.Equals(o.Status, "Fulfilled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(o.ProviderTxnId) &&
                    !string.Equals(o.ProviderTxnId, providerTxnId, StringComparison.Ordinal))
                    return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((false, "txn_mismatch", o.Clone()));
                return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((true, null, o.Clone()));
            }

            if (!string.Equals(o.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((false, "bad_status", o.Clone()));

            string key = $"{o.Provider}|{providerTxnId}";
            if (!_txnIndex.TryAdd(key, orderId))
            {
                if (_txnIndex.TryGetValue(key, out var existing) && existing == orderId)
                { /* ok */ }
                else
                    return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((false, "txn_reuse", null));
            }

            o.ProviderTxnId = providerTxnId;
            o.Status = "Paid";
            o.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((true, null, o.Clone()));
        }
    }

    public Task<(bool Ok, string? Error, PaymentOrderRecord? Order)> TryMarkFulfilledAsync(string orderId, string mailId, CancellationToken ct = default)
    {
        if (!_byId.TryGetValue(orderId, out var o))
            return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((false, "order_not_found", null));
        lock (o)
        {
            if (string.Equals(o.Status, "Fulfilled", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((true, null, o.Clone()));
            if (!string.Equals(o.Status, "Paid", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(o.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((false, "bad_status", o.Clone()));

            o.Status = "Fulfilled";
            o.FulfilledMailId = mailId;
            o.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<(bool, string?, PaymentOrderRecord?)>((true, null, o.Clone()));
        }
    }
}

internal sealed class SqlitePaymentStore : IPaymentStore
{
    private readonly string _cs;
    public SqlitePaymentStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS payment_orders (
              order_id TEXT PRIMARY KEY,
              account_id TEXT NOT NULL,
              character_id TEXT NOT NULL,
              product_id TEXT NOT NULL,
              price_cents INTEGER NOT NULL,
              reward_gold INTEGER NOT NULL DEFAULT 0,
              reward_item_id TEXT NOT NULL DEFAULT '',
              reward_item_count INTEGER NOT NULL DEFAULT 0,
              status TEXT NOT NULL,
              provider TEXT NOT NULL DEFAULT 'mock',
              provider_txn_id TEXT NOT NULL DEFAULT '',
              mock_pay_token TEXT NOT NULL DEFAULT '',
              fulfilled_mail_id TEXT NOT NULL DEFAULT '',
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_payment_txn ON payment_orders(provider, provider_txn_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PaymentOrderRecord> InsertAsync(PaymentOrderRecord order, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO payment_orders(order_id,account_id,character_id,product_id,price_cents,reward_gold,reward_item_id,reward_item_count,status,provider,provider_txn_id,mock_pay_token,fulfilled_mail_id,created_at,updated_at)
            VALUES($id,$a,$c,$p,$pc,$rg,$ri,$rc,$s,$pr,$pt,$mt,$fm,$ca,$ua)
            """;
        Bind(cmd, order);
        await cmd.ExecuteNonQueryAsync(ct);
        return order.Clone();
    }

    public async Task<PaymentOrderRecord?> GetByIdAsync(string orderId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM payment_orders WHERE order_id=$id";
        cmd.Parameters.AddWithValue("$id", orderId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return Read(r);
    }

    public async Task<PaymentOrderRecord?> GetByProviderTxnAsync(string provider, string providerTxnId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM payment_orders WHERE provider=$p AND provider_txn_id=$t LIMIT 1";
        cmd.Parameters.AddWithValue("$p", provider);
        cmd.Parameters.AddWithValue("$t", providerTxnId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return Read(r);
    }

    public async Task<(bool Ok, string? Error, PaymentOrderRecord? Order)> TryMarkPaidAsync(string orderId, string providerTxnId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT * FROM payment_orders WHERE order_id=$id";
            sel.Parameters.AddWithValue("$id", orderId);
            await using var r = await sel.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return (false, "order_not_found", null);
            var cur = Read(r);
            await r.DisposeAsync();

            if (string.Equals(cur.Status, "Fulfilled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cur.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(cur.ProviderTxnId) &&
                    !string.Equals(cur.ProviderTxnId, providerTxnId, StringComparison.Ordinal))
                    return (false, "txn_mismatch", cur);
                await tx.CommitAsync(ct);
                return (true, null, cur);
            }

            if (!string.Equals(cur.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                return (false, "bad_status", cur);

            await using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE payment_orders SET status='Paid', provider_txn_id=$t, updated_at=$u WHERE order_id=$id AND status='Pending'";
            upd.Parameters.AddWithValue("$t", providerTxnId);
            upd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
            upd.Parameters.AddWithValue("$id", orderId);
            int n = await upd.ExecuteNonQueryAsync(ct);
            if (n == 0)
            {
                await tx.RollbackAsync(ct);
                var again = await GetByIdAsync(orderId, ct);
                return (again is not null && (again.Status is "Paid" or "Fulfilled"), null, again);
            }
            await tx.CommitAsync(ct);
            cur.Status = "Paid";
            cur.ProviderTxnId = providerTxnId;
            cur.UpdatedAt = DateTimeOffset.UtcNow;
            return (true, null, cur);
        }
    }

    public async Task<(bool Ok, string? Error, PaymentOrderRecord? Order)> TryMarkFulfilledAsync(string orderId, string mailId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var upd = conn.CreateCommand();
        upd.CommandText = """
            UPDATE payment_orders SET status='Fulfilled', fulfilled_mail_id=$m, updated_at=$u
            WHERE order_id=$id AND status IN ('Paid','Pending')
            """;
        upd.Parameters.AddWithValue("$m", mailId);
        upd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        upd.Parameters.AddWithValue("$id", orderId);
        int n = await upd.ExecuteNonQueryAsync(ct);
        var cur = await GetByIdAsync(orderId, ct);
        if (cur is null) return (false, "order_not_found", null);
        if (string.Equals(cur.Status, "Fulfilled", StringComparison.OrdinalIgnoreCase))
            return (true, null, cur);
        if (n == 0) return (false, "bad_status", cur);
        return (true, null, cur);
    }

    private static void Bind(SqliteCommand cmd, PaymentOrderRecord o)
    {
        cmd.Parameters.AddWithValue("$id", o.OrderId);
        cmd.Parameters.AddWithValue("$a", o.AccountId);
        cmd.Parameters.AddWithValue("$c", o.CharacterId);
        cmd.Parameters.AddWithValue("$p", o.ProductId);
        cmd.Parameters.AddWithValue("$pc", o.PriceCents);
        cmd.Parameters.AddWithValue("$rg", o.RewardGold);
        cmd.Parameters.AddWithValue("$ri", o.RewardItemId ?? "");
        cmd.Parameters.AddWithValue("$rc", o.RewardItemCount);
        cmd.Parameters.AddWithValue("$s", o.Status);
        cmd.Parameters.AddWithValue("$pr", o.Provider);
        cmd.Parameters.AddWithValue("$pt", o.ProviderTxnId ?? "");
        cmd.Parameters.AddWithValue("$mt", o.MockPayToken ?? "");
        cmd.Parameters.AddWithValue("$fm", o.FulfilledMailId ?? "");
        cmd.Parameters.AddWithValue("$ca", o.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua", o.UpdatedAt.ToString("O"));
    }

    private static PaymentOrderRecord Read(SqliteDataReader r) => new()
    {
        OrderId = r.GetString(r.GetOrdinal("order_id")),
        AccountId = r.GetString(r.GetOrdinal("account_id")),
        CharacterId = r.GetString(r.GetOrdinal("character_id")),
        ProductId = r.GetString(r.GetOrdinal("product_id")),
        PriceCents = r.GetInt32(r.GetOrdinal("price_cents")),
        RewardGold = r.GetInt32(r.GetOrdinal("reward_gold")),
        RewardItemId = r.GetString(r.GetOrdinal("reward_item_id")),
        RewardItemCount = r.GetInt32(r.GetOrdinal("reward_item_count")),
        Status = r.GetString(r.GetOrdinal("status")),
        Provider = r.GetString(r.GetOrdinal("provider")),
        ProviderTxnId = r.GetString(r.GetOrdinal("provider_txn_id")),
        MockPayToken = r.GetString(r.GetOrdinal("mock_pay_token")),
        FulfilledMailId = r.GetString(r.GetOrdinal("fulfilled_mail_id")),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
        UpdatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("updated_at")))
    };
}

/// <summary>Postgres payment store — same schema as Sqlite for Phase CD sandbox.</summary>
internal sealed class PostgresPaymentStore : IPaymentStore
{
    private readonly string _cs;
    public PostgresPaymentStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS payment_orders (
              order_id TEXT PRIMARY KEY,
              account_id TEXT NOT NULL,
              character_id TEXT NOT NULL,
              product_id TEXT NOT NULL,
              price_cents INTEGER NOT NULL,
              reward_gold INTEGER NOT NULL DEFAULT 0,
              reward_item_id TEXT NOT NULL DEFAULT '',
              reward_item_count INTEGER NOT NULL DEFAULT 0,
              status TEXT NOT NULL,
              provider TEXT NOT NULL DEFAULT 'mock',
              provider_txn_id TEXT NOT NULL DEFAULT '',
              mock_pay_token TEXT NOT NULL DEFAULT '',
              fulfilled_mail_id TEXT NOT NULL DEFAULT '',
              created_at TIMESTAMPTZ NOT NULL,
              updated_at TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_payment_txn ON payment_orders(provider, provider_txn_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PaymentOrderRecord> InsertAsync(PaymentOrderRecord order, CancellationToken ct = default)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO payment_orders(order_id,account_id,character_id,product_id,price_cents,reward_gold,reward_item_id,reward_item_count,status,provider,provider_txn_id,mock_pay_token,fulfilled_mail_id,created_at,updated_at)
            VALUES(@id,@a,@c,@p,@pc,@rg,@ri,@rc,@s,@pr,@pt,@mt,@fm,@ca,@ua)
            """;
        cmd.Parameters.AddWithValue("id", order.OrderId);
        cmd.Parameters.AddWithValue("a", order.AccountId);
        cmd.Parameters.AddWithValue("c", order.CharacterId);
        cmd.Parameters.AddWithValue("p", order.ProductId);
        cmd.Parameters.AddWithValue("pc", order.PriceCents);
        cmd.Parameters.AddWithValue("rg", order.RewardGold);
        cmd.Parameters.AddWithValue("ri", order.RewardItemId ?? "");
        cmd.Parameters.AddWithValue("rc", order.RewardItemCount);
        cmd.Parameters.AddWithValue("s", order.Status);
        cmd.Parameters.AddWithValue("pr", order.Provider);
        cmd.Parameters.AddWithValue("pt", order.ProviderTxnId ?? "");
        cmd.Parameters.AddWithValue("mt", order.MockPayToken ?? "");
        cmd.Parameters.AddWithValue("fm", order.FulfilledMailId ?? "");
        cmd.Parameters.AddWithValue("ca", order.CreatedAt.UtcDateTime);
        cmd.Parameters.AddWithValue("ua", order.UpdatedAt.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(ct);
        return order.Clone();
    }

    public async Task<PaymentOrderRecord?> GetByIdAsync(string orderId, CancellationToken ct = default)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT order_id,account_id,character_id,product_id,price_cents,reward_gold,reward_item_id,reward_item_count,status,provider,provider_txn_id,mock_pay_token,fulfilled_mail_id,created_at,updated_at FROM payment_orders WHERE order_id=@id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return Read(r);
    }

    public async Task<PaymentOrderRecord?> GetByProviderTxnAsync(string provider, string providerTxnId, CancellationToken ct = default)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT order_id,account_id,character_id,product_id,price_cents,reward_gold,reward_item_id,reward_item_count,status,provider,provider_txn_id,mock_pay_token,fulfilled_mail_id,created_at,updated_at FROM payment_orders WHERE provider=@p AND provider_txn_id=@t LIMIT 1";
        cmd.Parameters.AddWithValue("p", provider);
        cmd.Parameters.AddWithValue("t", providerTxnId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return Read(r);
    }

    public async Task<(bool Ok, string? Error, PaymentOrderRecord? Order)> TryMarkPaidAsync(string orderId, string providerTxnId, CancellationToken ct = default)
    {
        var cur = await GetByIdAsync(orderId, ct);
        if (cur is null) return (false, "order_not_found", null);
        if (cur.Status is "Paid" or "Fulfilled")
        {
            if (!string.IsNullOrEmpty(cur.ProviderTxnId) && cur.ProviderTxnId != providerTxnId)
                return (false, "txn_mismatch", cur);
            return (true, null, cur);
        }
        await using var conn = new Npgsql.NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE payment_orders SET status='Paid', provider_txn_id=@t, updated_at=@u WHERE order_id=@id AND status='Pending'";
        upd.Parameters.AddWithValue("t", providerTxnId);
        upd.Parameters.AddWithValue("u", DateTimeOffset.UtcNow.UtcDateTime);
        upd.Parameters.AddWithValue("id", orderId);
        await upd.ExecuteNonQueryAsync(ct);
        return (true, null, await GetByIdAsync(orderId, ct));
    }

    public async Task<(bool Ok, string? Error, PaymentOrderRecord? Order)> TryMarkFulfilledAsync(string orderId, string mailId, CancellationToken ct = default)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE payment_orders SET status='Fulfilled', fulfilled_mail_id=@m, updated_at=@u WHERE order_id=@id AND status IN ('Paid','Pending')";
        upd.Parameters.AddWithValue("m", mailId);
        upd.Parameters.AddWithValue("u", DateTimeOffset.UtcNow.UtcDateTime);
        upd.Parameters.AddWithValue("id", orderId);
        await upd.ExecuteNonQueryAsync(ct);
        var cur = await GetByIdAsync(orderId, ct);
        if (cur is null) return (false, "order_not_found", null);
        if (cur.Status == "Fulfilled") return (true, null, cur);
        return (false, "bad_status", cur);
    }

    private static PaymentOrderRecord Read(System.Data.Common.DbDataReader r) => new()
    {
        OrderId = r.GetString(0),
        AccountId = r.GetString(1),
        CharacterId = r.GetString(2),
        ProductId = r.GetString(3),
        PriceCents = r.GetInt32(4),
        RewardGold = r.GetInt32(5),
        RewardItemId = r.GetString(6),
        RewardItemCount = r.GetInt32(7),
        Status = r.GetString(8),
        Provider = r.GetString(9),
        ProviderTxnId = r.GetString(10),
        MockPayToken = r.GetString(11),
        FulfilledMailId = r.GetString(12),
        CreatedAt = r.GetFieldValue<DateTime>(13) is var ca ? new DateTimeOffset(DateTime.SpecifyKind(ca, DateTimeKind.Utc)) : DateTimeOffset.UtcNow,
        UpdatedAt = r.GetFieldValue<DateTime>(14) is var ua ? new DateTimeOffset(DateTime.SpecifyKind(ua, DateTimeKind.Utc)) : DateTimeOffset.UtcNow
    };
}
