using System.ComponentModel;
using System.Text;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class DescribeTableTool(DatabaseService databaseService)
{
    [McpServerTool, Description("Describes the structure of a table including columns, types, nullability, primary keys and foreign keys.")]
    public async Task<string> DescribeTable(
        [Description("Database name")] string database,
        [Description("Schema name")] string schema,
        [Description("Table name")] string table)
    {
        if (!databaseService.IsConnected)
            return "Not connected. Please call connect first.";

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync(database);
            await using var command = connection.CreateCommand();
            command.CommandText = SqlServerQueries.DescribeTable;
            command.Parameters.AddWithValue("schema", schema);
            command.Parameters.AddWithValue("table", table);

            var result = new StringBuilder();
            result.AppendLine($"Table: {schema}.{table}");
            result.AppendLine(new string('-', 80));
            result.AppendLine($"{"Column",-30} {"Type",-20} {"Nullable",-10} {"PK",-5} {"FK",-5} {"References",-30}");
            result.AppendLine(new string('-', 80));

            await using var reader = await command.ExecuteReaderAsync();
            var hasRows = false;

            while (await reader.ReadAsync())
            {
                hasRows = true;
                var columnName = reader.GetString(0);
                var dataType = reader.GetString(1);
                var isNullable = reader.GetString(2);
                var isPrimaryKey = reader.GetString(4);
                var isForeignKey = reader.GetString(5);
                var foreignTable = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                var foreignColumn = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
                var references = isForeignKey == "YES" ? $"{foreignTable}.{foreignColumn}" : string.Empty;

                result.AppendLine($"{columnName,-30} {dataType,-20} {isNullable,-10} {isPrimaryKey,-5} {isForeignKey,-5} {references,-30}");
            }

            return hasRows ? result.ToString() : $"Table '{schema}.{table}' not found in database '{database}'.";
        }
        catch (Exception ex)
        {
            return $"Error describing table: {ex.Message}";
        }
    }
}
