using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Mcp.SqlServer.Services;

public sealed record PendingWrite(
    string WriteId,
    string ConnectionId,
    string Host,
    int Port,
    string Database,
    WriteStatement Statement,
    string SqlHash,
    string TableName,
    int ExpectedRows,
    DateTimeOffset ExpiresAt);

public enum PendingWriteStatus
{
    Valid,
    NotFound,
    Expired,
    ConnectionMismatch,
    SqlMismatch,
    CodeRequired,
    InvalidCode,
    AttemptsExhausted
}

public sealed class PendingWriteStore(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public const int MaxCodeAttempts = 3;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public static string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string HashSql(string sql) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

    public PendingWrite Register(string connectionId, string host, int port, string database, WriteStatement statement, string tableName, int expectedRows, string? code)
    {
        RemoveExpired();

        var writeId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var pending = new PendingWrite(
            writeId,
            connectionId,
            host,
            port,
            database,
            statement,
            HashSql(statement.Sql),
            tableName,
            expectedRows,
            timeProvider.GetUtcNow() + Lifetime);

        _entries[writeId] = new Entry(pending, code is null ? null : HashCode(writeId, code));
        return pending;
    }

    public (PendingWriteStatus Status, PendingWrite? Pending) Validate(string writeId, string? connectionId)
    {
        if (string.IsNullOrWhiteSpace(writeId) || !_entries.TryGetValue(writeId, out var entry))
            return (PendingWriteStatus.NotFound, null);

        if (timeProvider.GetUtcNow() >= entry.Pending.ExpiresAt)
        {
            _entries.TryRemove(writeId, out _);
            return (PendingWriteStatus.Expired, null);
        }

        if (!string.Equals(entry.Pending.ConnectionId, connectionId, StringComparison.Ordinal))
            return (PendingWriteStatus.ConnectionMismatch, null);

        if (!string.Equals(entry.Pending.SqlHash, HashSql(entry.Pending.Statement.Sql), StringComparison.Ordinal))
            return (PendingWriteStatus.SqlMismatch, null);

        return (PendingWriteStatus.Valid, entry.Pending);
    }

    public (PendingWriteStatus Status, int RemainingAttempts) VerifyCode(string writeId, string? code)
    {
        if (!_entries.TryGetValue(writeId, out var entry) || entry.CodeHash is null)
            return (PendingWriteStatus.NotFound, 0);

        if (string.IsNullOrWhiteSpace(code))
            return (PendingWriteStatus.CodeRequired, MaxCodeAttempts - entry.FailedAttempts);

        if (CryptographicOperations.FixedTimeEquals(entry.CodeHash, HashCode(writeId, code.Trim())))
            return (PendingWriteStatus.Valid, MaxCodeAttempts - entry.FailedAttempts);

        var failedAttempts = entry.RegisterFailedAttempt();
        if (failedAttempts < MaxCodeAttempts)
            return (PendingWriteStatus.InvalidCode, MaxCodeAttempts - failedAttempts);

        _entries.TryRemove(writeId, out _);
        return (PendingWriteStatus.AttemptsExhausted, 0);
    }

    public bool TryConsume(string writeId) => _entries.TryRemove(writeId, out _);

    private void RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();

        foreach (var (writeId, entry) in _entries)
        {
            if (now >= entry.Pending.ExpiresAt)
                _entries.TryRemove(writeId, out _);
        }
    }

    private static byte[] HashCode(string writeId, string code) => SHA256.HashData(Encoding.UTF8.GetBytes($"{writeId}:{code}"));

    private sealed class Entry(PendingWrite pending, byte[]? codeHash)
    {
        private int _failedAttempts;

        public PendingWrite Pending { get; } = pending;
        public byte[]? CodeHash { get; } = codeHash;
        public int FailedAttempts => Volatile.Read(ref _failedAttempts);

        public int RegisterFailedAttempt() => Interlocked.Increment(ref _failedAttempts);
    }
}
