using System.Security.Cryptography;
using System.Text;
using Astrolune.Runtime.Core.Server;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Astrolune.Runtime.Core.Storage;

public sealed class SecureStorage : ISecureStorage, IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly ILogger<SecureStorage> _logger;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SecureStorage(string databasePath, ILogger<SecureStorage> logger)
    {
        _dbPath = Path.Combine(databasePath, "secure.db");
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync(cancellationToken);

        var createTable = @"
            CREATE TABLE IF NOT EXISTS secure_storage (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            )";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = createTable;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Secure storage initialized at {Path}", _dbPath);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                throw new InvalidOperationException("Storage not initialized");
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM secure_storage WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);

            var encrypted = await cmd.ExecuteScalarAsync(cancellationToken) as string;
            if (encrypted is null)
            {
                return null;
            }

            return Decrypt(encrypted);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                throw new InvalidOperationException("Storage not initialized");
            }

            var encrypted = Encrypt(value);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO secure_storage (key, value, created_at, updated_at)
                VALUES (@key, @value, @now, @now)
                ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = @now";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", encrypted);
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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                throw new InvalidOperationException("Storage not initialized");
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM secure_storage WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string cipherText)
    {
        var encrypted = Convert.FromBase64String(cipherText);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        _lock.Dispose();
    }
}
