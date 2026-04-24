using System.Collections.Concurrent;
using System.Text.Json;
using Astrolune.Runtime.Core.Server;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Storage;

public sealed class StateManager : IStateManager, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _dbPath;
    private readonly ILogger<StateManager> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Timer? _cleanupTimer;

    public StateManager(string databasePath, ILogger<StateManager> logger)
    {
        _dbPath = Path.Combine(databasePath, "state.db");
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync(cancellationToken);

        var createTable = @"
            CREATE TABLE IF NOT EXISTS state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                expires_at INTEGER,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            )";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = createTable;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _cleanupTimer = new Timer(
            _ => _ = CleanupExpiredAsync(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));

        _logger.LogInformation("State manager initialized at {Path}", _dbPath);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            if (!cached.IsExpired)
            {
                return JsonSerializer.Deserialize<T>(cached.Value, JsonOptions);
            }

            _cache.TryRemove(key, out _);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                throw new InvalidOperationException("State manager not initialized");
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT value, expires_at FROM state
                WHERE key = @key AND (expires_at IS NULL OR expires_at > @now)";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return default;
            }

            var json = reader.GetString(0);
            var expiresAt = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);

            _cache[key] = new CacheEntry(json, expiresAt);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = ttl.HasValue ? now + (long)ttl.Value.TotalSeconds : (long?)null;

        _cache[key] = new CacheEntry(json, expiresAt);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                throw new InvalidOperationException("State manager not initialized");
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO state (key, value, expires_at, created_at, updated_at)
                VALUES (@key, @value, @expires_at, @now, @now)
                ON CONFLICT(key) DO UPDATE SET value = @value, expires_at = @expires_at, updated_at = @now";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", json);
            cmd.Parameters.AddWithValue("@expires_at", expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@now", now);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.TryRemove(key, out _);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                throw new InvalidOperationException("State manager not initialized");
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM state WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CleanupExpiredAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsExpired)
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }

        await _lock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                return;
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM state WHERE expires_at IS NOT NULL AND expires_at <= @now";
            cmd.Parameters.AddWithValue("@now", now);
            var deleted = await cmd.ExecuteNonQueryAsync();

            if (deleted > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired state entries", deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup expired state entries");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cleanupTimer?.Dispose();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        _lock.Dispose();
    }

    private sealed record CacheEntry(string Value, long? ExpiresAt)
    {
        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
