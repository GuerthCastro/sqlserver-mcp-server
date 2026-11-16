using System.ComponentModel;
using System.Data.Common;
using Mcp.SqlServer.Configuration;
using Mcp.SqlServer.Services;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

[McpServerToolType]
public class ExecuteQueryTool(DatabaseService databaseService, RuntimeOptions options, ILogger<ExecuteQueryTool> logger)
{
    [McpServerTool, Description(
        "Executes a single read-only query (SELECT or WITH ... SELECT) against the specified database. Runs inside a transaction that is always rolled back. " +
        "Results are paged: each call returns rows starting at offset until the page row limit or the page character limit is reached, " +
        "and the first line of the response is 'rows X-Y · nextOffset: N · hasMore: true|false'. " +
        "For totals, use COUNT, SUM or GROUP BY instead of reading every row. " +
        "To walk through a result, repeat the same query with offset = nextOffset; offset > 0 requires an ORDER BY on a unique key in the outer query. " +
        "Each page re-executes the query and skips offset rows, so for very long walks prefer keyset pagination (WHERE id > last_id ORDER BY id) with offset 0. " +
        "Pages are not consistent if the data changes between calls.")]
    public async Task<string> ExecuteQuery(
        [Description("Database name")] string database,
        [Description("SQL query to execute (SELECT or WITH)")] string sql,
        [Description("Number of rows to skip before the page starts. Use the nextOffset of the previous page. Defaults to 0")] int offset = 0,
        [Description("connectionId returned by connect")] string? connectionId = null)
    {
        if (!databaseService.TryGet(connectionId, out var configuration))
            return ToolMessages.NotConnected;

        var validationError = QueryGuard.Validate(sql, SqlServerQueries.ForbiddenKeywords, out var normalizedSql)
            ?? QueryGuard.ValidatePaging(normalizedSql, offset);
        if (validationError != null)
            return validationError;

        try
        {
            await using var connection = await databaseService.OpenConnectionAsync(configuration, database);
            await using var transaction = connection.BeginTransaction();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = normalizedSql;
            command.CommandTimeout = options.CommandTimeoutSeconds;

            var output = await ReadPageAsync(command, offset);

            try
            {
                transaction.Rollback();
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                logger.LogWarning("Rollback after ExecuteQuery failed: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
            }

            return output;
        }
        catch (Exception ex)
        {
            return $"Error executing query: {ex.Message}";
        }
    }

    private async Task<string> ReadPageAsync(DbCommand command, int offset)
    {
        var readToEnd = false;
        var reader = await command.ExecuteReaderAsync();
        try
        {
            var page = await ResultPager.ReadPageAsync(reader, offset, options.PageMaxRows, options.PageMaxCharacters);
            readToEnd = !page.HasMore;
            return page.Text;
        }
        finally
        {
            if (!readToEnd)
            {
                try
                {
                    command.Cancel();
                }
                catch (Exception ex) when (ex is SqlException or InvalidOperationException)
                {
                    logger.LogWarning("Cancel of partially read ExecuteQuery failed: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
                }
            }

            try
            {
                await reader.DisposeAsync();
            }
            catch (SqlException ex) when (!readToEnd)
            {
                logger.LogDebug("Partially read ExecuteQuery was cancelled as expected: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                logger.LogWarning("Closing ExecuteQuery reader failed: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
            }
        }
    }
}
