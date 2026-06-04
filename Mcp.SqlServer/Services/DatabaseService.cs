using Mcp.SqlServer.Configuration;
using Microsoft.Data.SqlClient;

namespace Mcp.SqlServer.Services;

public class DatabaseService
{
    private ServerConfiguration _configuration = new();

    public bool IsConnected => _configuration.IsConnected;

    public void Configure(string host, int? port, string username, string password, string? database)
    {
        _configuration = new ServerConfiguration
        {
            Host = host,
            Port = port,
            Username = username,
            Password = password,
            Database = database
        };
    }

    public string GetConnectionString(string? database = null)
    {
        var targetDatabase = database ?? _configuration.Database ?? "master";

        return new SqlConnectionStringBuilder
        {
            DataSource = $"{_configuration.Host},{_configuration.ResolvedPort}",
            UserID = _configuration.Username,
            Password = _configuration.Password,
            InitialCatalog = targetDatabase,
            TrustServerCertificate = true
        }.ToString();
    }

    public async Task<SqlConnection> OpenConnectionAsync(string? database = null)
    {
        var connection = new SqlConnection(GetConnectionString(database));
        await connection.OpenAsync();
        return connection;
    }

    public ServerConfiguration GetConfiguration() => _configuration;
}