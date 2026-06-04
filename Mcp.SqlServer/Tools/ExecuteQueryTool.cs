using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class ExecuteQueryTool(DatabaseService databaseService)
{
    [McpServerTool, Description("Executes a read-only SELECT query against the specified database.")]
    public async Task<string> ExecuteQuery(
        [Description("Database name")] string database,
        [Description("SQL SELECT query to execute")] string sql)
    {
        if (!databaseService.IsConnected)
            return "Not connected. Please call connect first.";

        var upperSql = sql.ToUpperInvariant().Trim();

        if (!upperSql.StartsWith("SELECT"))
            return "Only SELECT queries are allowed.";

        var forbidden = SqlServerQueries.ForbiddenKeywords.FirstOrDefault(k => Regex.IsMatch(upperSql, $@"\b{k}\b"));
        if (forbidden != null)
            return $"Query contains forbidden keyword: {forbidden}. Only read-only SELECT queries are allowed.";

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync(database);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();

            var result = new StringBuilder();
            var columnCount = reader.FieldCount;
            var headers = Enumerable.Range(0, columnCount).Select(i => reader.GetName(i));

            result.AppendLine(string.Join(" | ", headers));
            result.AppendLine(new string('-', 80));

            var rowCount = 0;
            while (await reader.ReadAsync())
            {
                var row = Enumerable.Range(0, columnCount).Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString() ?? "NULL");
                result.AppendLine(string.Join(" | ", row));
                rowCount++;
            }

            result.AppendLine(new string('-', 80));
            result.AppendLine($"{rowCount} row(s) returned.");

            var output = result.ToString();
            if (output.Length > SqlServerQueries.MaxOutputCharacters)
                output = $"{output[..SqlServerQueries.MaxOutputCharacters]}\n\n[Output truncated. {rowCount} total rows, showing partial results.]";

            return output;
        }
        catch (Exception ex)
        {
            return $"Error executing query: {ex.Message}";
        }
    }
}
