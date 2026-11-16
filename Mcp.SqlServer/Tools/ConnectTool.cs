using System.ComponentModel;
using Mcp.SqlServer.Configuration;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class ConnectTool(DatabaseService databaseService)
{
    [McpServerTool, Description("Connects to a SQL Server instance and returns a connectionId. Pass that connectionId to every other tool. Port is optional, defaults to 1433.")]
    public async Task<string> Connect(
        [Description("SQL Server host or IP address")] string host,
        [Description("SQL Server port, defaults to 1433")] int? port,
        [Description("Username")] string username,
        [Description("Password")] string password,
        [Description("Default database, optional")] string? database)
    {
        var configuration = new ServerConfiguration
        {
            Host = host,
            Port = port,
            Username = username,
            Password = password,
            Database = database
        };

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync(configuration);
            var serverVersion = connection.ServerVersion;
            var connectionId = databaseService.Register(configuration);

            return $"Connected to SQL Server {serverVersion} at {host}:{configuration.ResolvedPort}.\n" +
                   $"connectionId: {connectionId}\n" +
                   "Use this connectionId in every other tool call.";
        }
        catch (Exception ex)
        {
            return $"Connection failed: {ex.Message}";
        }
    }

    [McpServerTool, Description("Closes a connection created with connect and forgets its credentials.")]
    public string Disconnect(
        [Description("connectionId returned by connect")] string? connectionId = null)
    {
        return databaseService.Remove(connectionId)
            ? "Disconnected."
            : "Unknown or expired connectionId.";
    }
}
