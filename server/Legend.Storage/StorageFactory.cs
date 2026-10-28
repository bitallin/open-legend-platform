namespace Legend.Storage;

public sealed class StorageOptions
{
    /// <summary>InMemory | Sqlite | Postgres</summary>
    public string Backend { get; set; } = "Sqlite";
    public string SqlitePath { get; set; } = "data/legend.db";
    public string? PostgresConnectionString { get; set; }
    public string? RedisConnectionString { get; set; }
}

public static class StorageFactory
{
    public static async Task<ILegendStorage> CreateAsync(StorageOptions options, CancellationToken ct = default)
    {
        string backend = (options.Backend ?? "Sqlite").Trim();
        ILegendStorage storage;

        if (string.Equals(backend, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            storage = new InMemoryStorage();
        }
        else if (string.Equals(backend, "Postgres", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(backend, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.PostgresConnectionString))
                throw new InvalidOperationException("Postgres backend requires PostgresConnectionString.");

            ISessionStore sessions;
            if (!string.IsNullOrWhiteSpace(options.RedisConnectionString))
                sessions = new RedisSessionStore(options.RedisConnectionString);
            else
                sessions = new InMemorySessionStore();

            storage = new PostgresStorage(options.PostgresConnectionString, sessions);
        }
        else
        {
            // default Sqlite (durable without Docker)
            storage = new SqliteStorage(options.SqlitePath);
        }

        await storage.InitializeAsync(ct);
        return storage;
    }

    public static StorageOptions FromEnvironment()
    {
        var opts = new StorageOptions();
        string? backend = Environment.GetEnvironmentVariable("LEGEND_STORAGE");
        if (!string.IsNullOrWhiteSpace(backend))
            opts.Backend = backend;

        string? sqlite = Environment.GetEnvironmentVariable("LEGEND_SQLITE_PATH");
        if (!string.IsNullOrWhiteSpace(sqlite))
            opts.SqlitePath = sqlite;

        string? pg = Environment.GetEnvironmentVariable("LEGEND_PG")
                     ?? Environment.GetEnvironmentVariable("LEGEND_POSTGRES")
                     ?? Environment.GetEnvironmentVariable("LEGEND_DB_CONN");
        if (!string.IsNullOrWhiteSpace(pg))
        {
            opts.PostgresConnectionString = pg;
            if (string.IsNullOrWhiteSpace(backend))
                opts.Backend = "Postgres";
        }

        string? redis = Environment.GetEnvironmentVariable("LEGEND_REDIS")
                     ?? Environment.GetEnvironmentVariable("LEGEND_REDIS_CONN");
        if (!string.IsNullOrWhiteSpace(redis))
            opts.RedisConnectionString = redis;

        // docker-compose defaults
        if (string.Equals(opts.Backend, "Postgres", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(opts.PostgresConnectionString))
        {
            opts.PostgresConnectionString = "Host=127.0.0.1;Port=5432;Database=legend_zone_01;Username=legend;Password=legend_dev_password";
        }
        if (string.Equals(opts.Backend, "Postgres", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(opts.RedisConnectionString))
        {
            opts.RedisConnectionString = "127.0.0.1:6379";
        }

        return opts;
    }
}
