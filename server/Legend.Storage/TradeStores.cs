using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Legend.Storage;

internal sealed class InMemoryTradeStore : ITradeStore
{
    private readonly ConcurrentDictionary<string, TradeListingRecord> _byId = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task EnsureSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<TradeListingRecord?> GetByIdAsync(string listingId, CancellationToken ct = default)
        => Task.FromResult(_byId.TryGetValue(listingId, out var r) ? r.Clone() : null);

    public Task<IReadOnlyList<TradeListingRecord>> ListActiveAsync(string? itemIdFilter = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var q = _byId.Values.Where(x => string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(itemIdFilter))
            {
                string f = itemIdFilter.Trim();
                q = q.Where(x => string.Equals(x.ItemId, f, StringComparison.OrdinalIgnoreCase));
            }
            var list = q.OrderBy(x => x.CreatedAt).Select(x => x.Clone()).ToList();
            return Task.FromResult<IReadOnlyList<TradeListingRecord>>(list);
        }
    }

    public Task<int> CountActiveBySellerAsync(string sellerCharacterId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            int count = _byId.Values.Count(x =>
                string.Equals(x.SellerCharacterId, sellerCharacterId, StringComparison.Ordinal) &&
                string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(count);
        }
    }

    public Task<TradeListingRecord> CreateAsync(TradeListingRecord listing, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(listing.ListingId))
                listing.ListingId = Guid.NewGuid().ToString("N");
            listing.Status = "active";
            listing.CreatedAt = DateTimeOffset.UtcNow;
            listing.UpdatedAt = listing.CreatedAt;
            _byId[listing.ListingId] = listing.Clone();
            return Task.FromResult(listing.Clone());
        }
    }

    public Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryClaimSoldAsync(
        string listingId, string buyerCharacterId, string buyerName, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(listingId, out var r))
                return Task.FromResult<(bool, string?, TradeListingRecord?)>((false, "挂单不存在", null));
            if (!string.Equals(r.Status, "active", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<(bool, string?, TradeListingRecord?)>((false, "挂单已售出或已下架", null));
            if (string.Equals(r.SellerCharacterId, buyerCharacterId, StringComparison.Ordinal))
                return Task.FromResult<(bool, string?, TradeListingRecord?)>((false, "不能购买自己的挂单", null));
            r.Status = "sold";
            r.BuyerCharacterId = buyerCharacterId;
            r.BuyerName = buyerName;
            r.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<(bool, string?, TradeListingRecord?)>((true, null, r.Clone()));
        }
    }

    public Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryCancelAsync(
        string listingId, string sellerCharacterId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(listingId, out var r))
                return Task.FromResult<(bool, string?, TradeListingRecord?)>((false, "挂单不存在", null));
            if (!string.Equals(r.SellerCharacterId, sellerCharacterId, StringComparison.Ordinal))
                return Task.FromResult<(bool, string?, TradeListingRecord?)>((false, "无权下架他人挂单", null));
            if (!string.Equals(r.Status, "active", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<(bool, string?, TradeListingRecord?)>((false, "挂单已售出或已下架", null));
            r.Status = "cancelled";
            r.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<(bool, string?, TradeListingRecord?)>((true, null, r.Clone()));
        }
    }
}

internal sealed class SqliteTradeStore : ITradeStore
{
    private readonly string _cs;
    public SqliteTradeStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS trade_listings (
              listing_id TEXT PRIMARY KEY,
              seller_character_id TEXT NOT NULL,
              seller_name TEXT NOT NULL,
              item_id TEXT NOT NULL,
              count INTEGER NOT NULL,
              unit_price_gold INTEGER NOT NULL,
              total_price_gold INTEGER NOT NULL,
              status TEXT NOT NULL,
              buyer_character_id TEXT NOT NULL DEFAULT '',
              buyer_name TEXT NOT NULL DEFAULT '',
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_trade_listings_status ON trade_listings(status);
            CREATE INDEX IF NOT EXISTS idx_trade_listings_seller ON trade_listings(seller_character_id, status);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TradeListingRecord?> GetByIdAsync(string listingId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
                   status, buyer_character_id, buyer_name, created_at, updated_at
            FROM trade_listings WHERE listing_id = $id
            """;
        cmd.Parameters.AddWithValue("$id", listingId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<IReadOnlyList<TradeListingRecord>> ListActiveAsync(string? itemIdFilter = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(itemIdFilter))
        {
            cmd.CommandText = """
                SELECT listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
                       status, buyer_character_id, buyer_name, created_at, updated_at
                FROM trade_listings WHERE status = 'active' ORDER BY created_at ASC
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
                       status, buyer_character_id, buyer_name, created_at, updated_at
                FROM trade_listings WHERE status = 'active' AND item_id = $item COLLATE NOCASE ORDER BY created_at ASC
                """;
            cmd.Parameters.AddWithValue("$item", itemIdFilter.Trim());
        }
        var list = new List<TradeListingRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Read(reader));
        return list;
    }

    public async Task<int> CountActiveBySellerAsync(string sellerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM trade_listings WHERE seller_character_id = $s AND status = 'active'";
        cmd.Parameters.AddWithValue("$s", sellerCharacterId);
        var val = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(val);
    }

    public async Task<TradeListingRecord> CreateAsync(TradeListingRecord listing, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(listing.ListingId))
            listing.ListingId = Guid.NewGuid().ToString("N");
        listing.Status = "active";
        listing.CreatedAt = DateTimeOffset.UtcNow;
        listing.UpdatedAt = listing.CreatedAt;

        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO trade_listings(
              listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
              status, buyer_character_id, buyer_name, created_at, updated_at)
            VALUES($id, $sc, $sn, $item, $cnt, $up, $tp, $st, $bc, $bn, $ca, $ua)
            """;
        cmd.Parameters.AddWithValue("$id", listing.ListingId);
        cmd.Parameters.AddWithValue("$sc", listing.SellerCharacterId);
        cmd.Parameters.AddWithValue("$sn", listing.SellerName);
        cmd.Parameters.AddWithValue("$item", listing.ItemId);
        cmd.Parameters.AddWithValue("$cnt", listing.Count);
        cmd.Parameters.AddWithValue("$up", listing.UnitPriceGold);
        cmd.Parameters.AddWithValue("$tp", listing.TotalPriceGold);
        cmd.Parameters.AddWithValue("$st", listing.Status);
        cmd.Parameters.AddWithValue("$bc", listing.BuyerCharacterId ?? string.Empty);
        cmd.Parameters.AddWithValue("$bn", listing.BuyerName ?? string.Empty);
        cmd.Parameters.AddWithValue("$ca", listing.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua", listing.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        return listing.Clone();
    }

    public async Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryClaimSoldAsync(
        string listingId, string buyerCharacterId, string buyerName, CancellationToken ct = default)
    {
        var cur = await GetByIdAsync(listingId, ct);
        if (cur is null) return (false, "挂单不存在", null);
        if (!string.Equals(cur.Status, "active", StringComparison.OrdinalIgnoreCase))
            return (false, "挂单已售出或已下架", null);
        if (string.Equals(cur.SellerCharacterId, buyerCharacterId, StringComparison.Ordinal))
            return (false, "不能购买自己的挂单", null);

        var now = DateTimeOffset.UtcNow;
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE trade_listings
            SET status = 'sold', buyer_character_id = $bc, buyer_name = $bn, updated_at = $ua
            WHERE listing_id = $id AND status = 'active'
            """;
        cmd.Parameters.AddWithValue("$bc", buyerCharacterId);
        cmd.Parameters.AddWithValue("$bn", buyerName);
        cmd.Parameters.AddWithValue("$ua", now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", listingId);
        int affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected == 0)
            return (false, "挂单已售出或已下架", null);
        cur.Status = "sold";
        cur.BuyerCharacterId = buyerCharacterId;
        cur.BuyerName = buyerName;
        cur.UpdatedAt = now;
        return (true, null, cur);
    }

    public async Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryCancelAsync(
        string listingId, string sellerCharacterId, CancellationToken ct = default)
    {
        var cur = await GetByIdAsync(listingId, ct);
        if (cur is null) return (false, "挂单不存在", null);
        if (!string.Equals(cur.SellerCharacterId, sellerCharacterId, StringComparison.Ordinal))
            return (false, "无权下架他人挂单", null);
        if (!string.Equals(cur.Status, "active", StringComparison.OrdinalIgnoreCase))
            return (false, "挂单已售出或已下架", null);

        var now = DateTimeOffset.UtcNow;
        await using var conn = new SqliteConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE trade_listings
            SET status = 'cancelled', updated_at = $ua
            WHERE listing_id = $id AND seller_character_id = $sc AND status = 'active'
            """;
        cmd.Parameters.AddWithValue("$ua", now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", listingId);
        cmd.Parameters.AddWithValue("$sc", sellerCharacterId);
        int affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected == 0)
            return (false, "挂单已售出或已下架", null);
        cur.Status = "cancelled";
        cur.UpdatedAt = now;
        return (true, null, cur);
    }

    private static TradeListingRecord Read(SqliteDataReader reader) => new()
    {
        ListingId = reader.GetString(0),
        SellerCharacterId = reader.GetString(1),
        SellerName = reader.GetString(2),
        ItemId = reader.GetString(3),
        Count = reader.GetInt32(4),
        UnitPriceGold = reader.GetInt32(5),
        TotalPriceGold = reader.GetInt32(6),
        Status = reader.GetString(7),
        BuyerCharacterId = reader.GetString(8),
        BuyerName = reader.GetString(9),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(10)),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(11))
    };
}

internal sealed class PostgresTradeStore : ITradeStore
{
    private readonly string _cs;
    public PostgresTradeStore(string cs) => _cs = cs;

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS trade_listings (
              listing_id TEXT PRIMARY KEY,
              seller_character_id TEXT NOT NULL,
              seller_name TEXT NOT NULL,
              item_id TEXT NOT NULL,
              count INTEGER NOT NULL,
              unit_price_gold INTEGER NOT NULL,
              total_price_gold INTEGER NOT NULL,
              status TEXT NOT NULL,
              buyer_character_id TEXT NOT NULL DEFAULT '',
              buyer_name TEXT NOT NULL DEFAULT '',
              created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
              updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_trade_listings_status ON trade_listings(status);
            CREATE INDEX IF NOT EXISTS idx_trade_listings_seller ON trade_listings(seller_character_id, status);
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TradeListingRecord?> GetByIdAsync(string listingId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
                   status, buyer_character_id, buyer_name, created_at, updated_at
            FROM trade_listings WHERE listing_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", listingId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<IReadOnlyList<TradeListingRecord>> ListActiveAsync(string? itemIdFilter = null, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand();
        cmd.Connection = conn;
        if (string.IsNullOrWhiteSpace(itemIdFilter))
        {
            cmd.CommandText = """
                SELECT listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
                       status, buyer_character_id, buyer_name, created_at, updated_at
                FROM trade_listings WHERE status = 'active' ORDER BY created_at ASC
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
                       status, buyer_character_id, buyer_name, created_at, updated_at
                FROM trade_listings WHERE status = 'active' AND LOWER(item_id) = LOWER(@item) ORDER BY created_at ASC
                """;
            cmd.Parameters.AddWithValue("item", itemIdFilter.Trim());
        }
        var list = new List<TradeListingRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Read(reader));
        return list;
    }

    public async Task<int> CountActiveBySellerAsync(string sellerCharacterId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(1) FROM trade_listings WHERE seller_character_id = @s AND status = 'active'", conn);
        cmd.Parameters.AddWithValue("s", sellerCharacterId);
        var val = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(val);
    }

    public async Task<TradeListingRecord> CreateAsync(TradeListingRecord listing, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(listing.ListingId))
            listing.ListingId = Guid.NewGuid().ToString("N");
        listing.Status = "active";
        listing.CreatedAt = DateTimeOffset.UtcNow;
        listing.UpdatedAt = listing.CreatedAt;

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO trade_listings(
              listing_id, seller_character_id, seller_name, item_id, count, unit_price_gold, total_price_gold,
              status, buyer_character_id, buyer_name, created_at, updated_at)
            VALUES(@id, @sc, @sn, @item, @cnt, @up, @tp, @st, @bc, @bn, @ca, @ua)
            """, conn);
        cmd.Parameters.AddWithValue("id", listing.ListingId);
        cmd.Parameters.AddWithValue("sc", listing.SellerCharacterId);
        cmd.Parameters.AddWithValue("sn", listing.SellerName);
        cmd.Parameters.AddWithValue("item", listing.ItemId);
        cmd.Parameters.AddWithValue("cnt", listing.Count);
        cmd.Parameters.AddWithValue("up", listing.UnitPriceGold);
        cmd.Parameters.AddWithValue("tp", listing.TotalPriceGold);
        cmd.Parameters.AddWithValue("st", listing.Status);
        cmd.Parameters.AddWithValue("bc", listing.BuyerCharacterId ?? string.Empty);
        cmd.Parameters.AddWithValue("bn", listing.BuyerName ?? string.Empty);
        cmd.Parameters.AddWithValue("ca", listing.CreatedAt);
        cmd.Parameters.AddWithValue("ua", listing.UpdatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
        return listing.Clone();
    }

    public async Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryClaimSoldAsync(
        string listingId, string buyerCharacterId, string buyerName, CancellationToken ct = default)
    {
        var cur = await GetByIdAsync(listingId, ct);
        if (cur is null) return (false, "挂单不存在", null);
        if (!string.Equals(cur.Status, "active", StringComparison.OrdinalIgnoreCase))
            return (false, "挂单已售出或已下架", null);
        if (string.Equals(cur.SellerCharacterId, buyerCharacterId, StringComparison.Ordinal))
            return (false, "不能购买自己的挂单", null);

        var now = DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE trade_listings
            SET status = 'sold', buyer_character_id = @bc, buyer_name = @bn, updated_at = @ua
            WHERE listing_id = @id AND status = 'active'
            """, conn);
        cmd.Parameters.AddWithValue("bc", buyerCharacterId);
        cmd.Parameters.AddWithValue("bn", buyerName);
        cmd.Parameters.AddWithValue("ua", now);
        cmd.Parameters.AddWithValue("id", listingId);
        int affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected == 0)
            return (false, "挂单已售出或已下架", null);
        cur.Status = "sold";
        cur.BuyerCharacterId = buyerCharacterId;
        cur.BuyerName = buyerName;
        cur.UpdatedAt = now;
        return (true, null, cur);
    }

    public async Task<(bool Ok, string? Error, TradeListingRecord? Listing)> TryCancelAsync(
        string listingId, string sellerCharacterId, CancellationToken ct = default)
    {
        var cur = await GetByIdAsync(listingId, ct);
        if (cur is null) return (false, "挂单不存在", null);
        if (!string.Equals(cur.SellerCharacterId, sellerCharacterId, StringComparison.Ordinal))
            return (false, "无权下架他人挂单", null);
        if (!string.Equals(cur.Status, "active", StringComparison.OrdinalIgnoreCase))
            return (false, "挂单已售出或已下架", null);

        var now = DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE trade_listings
            SET status = 'cancelled', updated_at = @ua
            WHERE listing_id = @id AND seller_character_id = @sc AND status = 'active'
            """, conn);
        cmd.Parameters.AddWithValue("ua", now);
        cmd.Parameters.AddWithValue("id", listingId);
        cmd.Parameters.AddWithValue("sc", sellerCharacterId);
        int affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected == 0)
            return (false, "挂单已售出或已下架", null);
        cur.Status = "cancelled";
        cur.UpdatedAt = now;
        return (true, null, cur);
    }

    private static TradeListingRecord Read(NpgsqlDataReader reader) => new()
    {
        ListingId = reader.GetString(0),
        SellerCharacterId = reader.GetString(1),
        SellerName = reader.GetString(2),
        ItemId = reader.GetString(3),
        Count = reader.GetInt32(4),
        UnitPriceGold = reader.GetInt32(5),
        TotalPriceGold = reader.GetInt32(6),
        Status = reader.GetString(7),
        BuyerCharacterId = reader.GetString(8),
        BuyerName = reader.GetString(9),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(10),
        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(11)
    };
}
