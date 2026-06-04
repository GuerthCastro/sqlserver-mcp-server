using System.ComponentModel;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class ListTablesTool(DatabaseService databaseService)
{
    [McpServerTool, Description("Lists all tables in the specified schema and database.")]
    public async Task<string> ListTables(
        [Description("Database name")] string database,
        [Description("Schema name")] string schema)
    {
        if (!databaseService.IsConnected)
            return "Not connected. Please call connect first.";

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync(database);
            await using var command = connection.CreateCommand();
            command.CommandText = SqlServerQueries.ListTables;
            command.Parameters.AddWithValue("schema", schema);

            var tables = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

            return tables.Count == 0 ? $"No tables found in schema '{schema}' of database '{database}'." : string.Join("\n", tables);
        }
        catch (Exception ex)
        {
            return $"Error listing tables: {ex.Message}";
        }
    }
}
