using System.ComponentModel;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class ListDatabasesTool(DatabaseService databaseService)
{
    [McpServerTool, Description("Lists all databases available on the connected SQL Server instance.")]
    public async Task<string> ListDatabases(
        [Description("connectionId returned by connect")] string? connectionId = null)
    {
        if (!databaseService.TryGet(connectionId, out var configuration))
            return ToolMessages.NotConnected;

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync(configuration);
            await using var command = connection.CreateCommand();
            command.CommandText = SqlServerQueries.ListDatabases;

            var databases = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                databases.Add(reader.GetString(0));

            return databases.Count == 0 ? "No databases found." : string.Join("\n", databases);
        }
        catch (Exception ex)
        {
            return $"Error listing databases: {ex.Message}";
        }
    }
}
