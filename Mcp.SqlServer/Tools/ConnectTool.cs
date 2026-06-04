using System.ComponentModel;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class ConnectTool(DatabaseService databaseService)
{
    [McpServerTool, Description("Connects to a SQL Server instance. Port is optional, defaults to 1433.")]
    public async Task<string> Connect(
        [Description("SQL Server host or IP address")] string host,
        [Description("SQL Server port, defaults to 1433")] int? port,
        [Description("Username")] string username,
        [Description("Password")] string password,
        [Description("Default database, optional")] string? database)
    {
        databaseService.Configure(host, port, username, password, database);

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync();
            var serverVersion = connection.ServerVersion;
            return $"Connected to SQL Server {serverVersion} at {host}:{databaseService.GetConfiguration().ResolvedPort}";
        }
        catch (Exception ex)
        {
            databaseService.Configure(string.Empty, null, string.Empty, string.Empty, null);
            return $"Connection failed: {ex.Message}";
        }
    }
}
