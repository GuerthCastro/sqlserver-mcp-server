using System.Collections.Concurrent;
using System.Security.Cryptography;
using Mcp.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace Mcp.SqlServer.Services;

public class DatabaseService(RuntimeOptions options)
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();

    public string Register(ServerConfiguration configuration)
    {
        PruneExpired();
        var connectionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _connections[connectionId] = new ConnectionEntry(configuration);
        return connectionId;
    }

    public bool TryGet(string? connectionId, out ServerConfiguration configuration)
    {
        PruneExpired();

        if (!string.IsNullOrWhiteSpace(connectionId) && _connections.TryGetValue(connectionId, out var entry))
        {
            entry.Touch();
            configuration = entry.Configuration;
            return true;
        }

        configuration = new ServerConfiguration();
        return false;
    }

    public bool Remove(string? connectionId) => !string.IsNullOrWhiteSpace(connectionId) && _connections.TryRemove(connectionId, out _);

    public string GetConnectionString(ServerConfiguration configuration, string? database = null)
    {
        var targetDatabase = database ?? configuration.Database ?? "master";

        return new SqlConnectionStringBuilder
        {
            DataSource = $"{configuration.Host},{configuration.ResolvedPort}",
            UserID = configuration.Username,
            Password = configuration.Password,
            InitialCatalog = targetDatabase,
            TrustServerCertificate = true,
            ConnectTimeout = 10,
            CommandTimeout = options.CommandTimeoutSeconds,
            ApplicationName = "sqlserver-mcp-server"
        }.ToString();
    }

    public async Task<SqlConnection> OpenConnectionAsync(ServerConfiguration configuration, string? database = null)
    {
        var connection = new SqlConnection(GetConnectionString(configuration, database));
        await connection.OpenAsync();
        return connection;
    }

    private void PruneExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - options.ConnectionIdleTimeout;

        foreach (var (key, entry) in _connections)
        {
            if (entry.LastUsedUtc < cutoff)
                _connections.TryRemove(key, out _);
        }
    }

    private sealed class ConnectionEntry(ServerConfiguration configuration)
    {
        public ServerConfiguration Configuration { get; } = configuration;
        public DateTimeOffset LastUsedUtc { get; private set; } = DateTimeOffset.UtcNow;
        public void Touch() => LastUsedUtc = DateTimeOffset.UtcNow;
    }
}
