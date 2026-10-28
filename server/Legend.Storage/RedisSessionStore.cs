using System.Text.Json;
using StackExchange.Redis;

namespace Legend.Storage;

public sealed class RedisSessionStore : ISessionStore, IAsyncDisposable
{
    private readonly ConnectionMultiplexer _mux;
    private readonly IDatabase _db;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public RedisSessionStore(string connectionString)
    {
        _mux = ConnectionMultiplexer.Connect(connectionString);
        _db = _mux.GetDatabase();
    }

    public Task EnsureReadyAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task<string> CreateSessionAsync(string accountId, string username, TimeSpan ttl, CancellationToken ct = default)
    {
        string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var record = new SessionRecord
        {
            Token = token,
            AccountId = accountId,
            Username = username,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };
        string json = JsonSerializer.Serialize(record, JsonOpts);
        await _db.StringSetAsync(Key(token), json, ttl);
        await _db.StringSetAsync(OnlineKey(accountId), "1", ttl);
        return token;
    }

    public async Task<SessionRecord?> GetSessionAsync(string token, CancellationToken ct = default)
    {
        RedisValue v = await _db.StringGetAsync(Key(token));
        if (v.IsNullOrEmpty)
            return null;
        return JsonSerializer.Deserialize<SessionRecord>((string)v!, JsonOpts);
    }

    public async Task BindCharacterAsync(string token, string characterId, CancellationToken ct = default)
    {
        var s = await GetSessionAsync(token, ct);
        if (s is null)
            return;
        s.CharacterId = characterId;
        TimeSpan? ttl = await _db.KeyTimeToLiveAsync(Key(token));
        await _db.StringSetAsync(Key(token), JsonSerializer.Serialize(s, JsonOpts), ttl);
    }

    public async Task SetOnlineAsync(string accountId, bool online, CancellationToken ct = default)
    {
        if (online)
            await _db.StringSetAsync(OnlineKey(accountId), "1", TimeSpan.FromHours(24));
        else
            await _db.KeyDeleteAsync(OnlineKey(accountId));
    }

    public async Task<bool> IsOnlineAsync(string accountId, CancellationToken ct = default)
        => await _db.KeyExistsAsync(OnlineKey(accountId));

    public async Task RevokeAsync(string token, CancellationToken ct = default)
    {
        var s = await GetSessionAsync(token, ct);
        await _db.KeyDeleteAsync(Key(token));
        if (s is not null)
            await _db.KeyDeleteAsync(OnlineKey(s.AccountId));
    }

    public async ValueTask DisposeAsync()
    {
        await _mux.CloseAsync();
        _mux.Dispose();
    }

    private static string Key(string token) => $"legend:session:{token}";
    private static string OnlineKey(string accountId) => $"legend:online:{accountId}";
}
