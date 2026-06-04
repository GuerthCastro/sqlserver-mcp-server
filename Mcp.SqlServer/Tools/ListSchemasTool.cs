using System.ComponentModel;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class ListSchemasTool(DatabaseService databaseService)
{
    [McpServerTool, Description("Lists all schemas in the specified database.")]
    public async Task<string> ListSchemas(
        [Description("Database name")] string database)
    {
        if (!databaseService.IsConnected)
            return "Not connected. Please call connect first.";

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync(database);
            await using var command = connection.CreateCommand();
            command.CommandText = SqlServerQueries.ListSchemas;

            var schemas = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                schemas.Add(reader.GetString(0));

            return schemas.Count == 0 ? $"No schemas found in database '{database}'." : string.Join("\n", schemas);
        }
        catch (Exception ex)
        {
            return $"Error listing schemas: {ex.Message}";
        }
    }
}
