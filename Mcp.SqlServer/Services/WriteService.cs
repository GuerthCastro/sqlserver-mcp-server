using System.Data;
using Mcp.SqlServer.Configuration;
using Mcp.SqlServer.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Data.SqlClient;

namespace Mcp.SqlServer.Services;

public sealed class WriteService(
    DatabaseService databaseService,
    RuntimeOptions options,
    PendingWriteStore pendingWrites,
    ConfirmationWebhook webhook,
    TimeProvider timeProvider,
    ILogger<WriteService> logger)
{
    private const string ElicitationNotSupported =
        "Write rejected: this server confirms writes through MCP elicitation, and the connected client does not support it. Use a client with elicitation support, or configure MCP_WRITE_CONFIRMATION=webhook.";

    private WriteOptions Write => options.Write;

    public async Task<string> PrepareAsync(McpServer server, string? connectionId, string database, string sql)
    {
        if (!databaseService.TryGet(connectionId, out var configuration))
            return ToolMessages.NotConnected;

        if (Write.Confirmation == WriteConfirmationMode.Elicitation && !SupportsElicitation(server))
            return ElicitationNotSupported;

        var guardError = WriteGuard.Analyze(sql, out var statement);
        if (guardError is not null)
            return $"Write rejected: {guardError}";

        try
        {
            var execution = await RunAsync(configuration, database, statement!, confirmedRows: null);
            if (execution.Error is not null)
                return $"Write rejected: {execution.Error}";

            var code = Write.Confirmation == WriteConfirmationMode.Webhook ? PendingWriteStore.GenerateCode() : null;
            var pending = pendingWrites.Register(connectionId!, configuration.Host, configuration.ResolvedPort, database, statement!, execution.Table!.DisplayName, execution.Rows, code);

            if (code is not null && !await webhook.SendCodeAsync(pending, code))
            {
                pendingWrites.TryConsume(pending.WriteId);
                return "Write rejected: the confirmation code could not be delivered to the confirmation webhook. Nothing was changed.";
            }

            Audit("prepared", pending.WriteId, database, statement!.Operation, execution.Rows);
            return FormatPrepared(pending, execution);
        }
        catch (Exception ex)
        {
            return $"Write rejected: {ex.Message}";
        }
    }

    public async Task<string> ConfirmAsync(McpServer server, string writeId, string? code, string? connectionId, CancellationToken cancellationToken)
    {
        var (status, pending) = pendingWrites.Validate(writeId, connectionId);
        switch (status)
        {
            case PendingWriteStatus.NotFound:
                return "Unknown or already used writeId. Prepare the write again with execute_write.";
            case PendingWriteStatus.Expired:
                return "The writeId expired (writes must be confirmed within 5 minutes). Prepare the write again with execute_write.";
            case PendingWriteStatus.ConnectionMismatch:
                return "The writeId was prepared with a different connectionId. Use the same connectionId that prepared it.";
            case PendingWriteStatus.SqlMismatch:
                return "The prepared statement does not match its hash. The write was not executed.";
        }

        if (!databaseService.TryGet(connectionId, out var configuration)
            || !string.Equals(configuration.Host, pending!.Host, StringComparison.OrdinalIgnoreCase)
            || configuration.ResolvedPort != pending.Port)
            return ToolMessages.NotConnected;

        var confirmationError = Write.Confirmation == WriteConfirmationMode.Webhook
            ? VerifyCode(pending, code)
            : await ConfirmWithElicitationAsync(server, pending, cancellationToken);

        if (confirmationError is not null)
            return confirmationError;

        if (!pendingWrites.TryConsume(pending.WriteId))
            return "Unknown or already used writeId. Prepare the write again with execute_write.";

        try
        {
            var execution = await RunAsync(configuration, pending.Database, pending.Statement, pending.ExpectedRows);
            if (execution.Error is not null)
            {
                Audit("aborted", pending.WriteId, pending.Database, pending.Statement.Operation, execution.Rows);
                return $"Write aborted and rolled back: {execution.Error}";
            }

            Audit("committed", pending.WriteId, pending.Database, pending.Statement.Operation, execution.Rows);
            return FormatCommitted(pending, execution);
        }
        catch (Exception ex)
        {
            Audit("failed", pending.WriteId, pending.Database, pending.Statement.Operation, 0);
            return $"Write failed and was rolled back: {ex.Message}";
        }
    }

    private string? VerifyCode(PendingWrite pending, string? code)
    {
        var (status, remainingAttempts) = pendingWrites.VerifyCode(pending.WriteId, code);

        switch (status)
        {
            case PendingWriteStatus.Valid:
                return null;
            case PendingWriteStatus.CodeRequired:
                return "A confirmation code is required. Ask the human for the 6-digit code sent to the confirmation channel.";
            case PendingWriteStatus.InvalidCode:
                Audit("invalid_code", pending.WriteId, pending.Database, pending.Statement.Operation, pending.ExpectedRows);
                return $"Invalid confirmation code. {remainingAttempts} attempt(s) left before the writeId is invalidated.";
            case PendingWriteStatus.AttemptsExhausted:
                Audit("invalidated", pending.WriteId, pending.Database, pending.Statement.Operation, pending.ExpectedRows);
                return "Invalid confirmation code. Too many failed attempts: the writeId was invalidated and nothing was changed.";
            default:
                return "Unknown or already used writeId. Prepare the write again with execute_write.";
        }
    }

    private async Task<string?> ConfirmWithElicitationAsync(McpServer server, PendingWrite pending, CancellationToken cancellationToken)
    {
        if (!SupportsElicitation(server))
            return ElicitationNotSupported;

        var request = new ElicitRequestParams
        {
            Message = $"Approve this database write?\n\n"
                      + $"{pending.Statement.Operation.ToString().ToUpperInvariant()} on {pending.TableName} "
                      + $"at {pending.Host}:{pending.Port}/{pending.Database}, {pending.ExpectedRows} row(s).\n\n"
                      + pending.Statement.Sql,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["approve"] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = "Approve write",
                        Description = $"Execute writeId {pending.WriteId}"
                    }
                },
                Required = ["approve"]
            }
        };

        var result = await server.ElicitAsync(request, cancellationToken);
        var approved = result.IsAccepted
            && result.Content is not null
            && result.Content.TryGetValue("approve", out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.True;

        if (approved)
            return null;

        pendingWrites.TryConsume(pending.WriteId);
        Audit("cancelled", pending.WriteId, pending.Database, pending.Statement.Operation, pending.ExpectedRows);
        return "The human did not approve the write. Nothing was changed and the writeId was discarded.";
    }

    private static bool SupportsElicitation(McpServer server)
    {
        var capability = server.ClientCapabilities?.Elicitation;
        return capability is not null && (capability.Form is not null || capability.Url is null);
    }

    private async Task<WriteExecution> RunAsync(ServerConfiguration configuration, string database, WriteStatement statement, int? confirmedRows)
    {
        await using var connection = await databaseService.OpenConnectionAsync(configuration, database);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var (table, tableError) = await WriteCatalog.ResolveAsync(connection, transaction, statement.TableName);
        if (table is null)
            return Fail(transaction, tableError!);

        var tableSupportError = ValidateSupportedTypes(table, statement);
        if (tableSupportError is not null)
            return Fail(transaction, tableSupportError);

        var targetsError = ResolveTargets(table, statement, out var targets);
        if (targetsError is not null)
            return Fail(transaction, targetsError);

        var beforeImage = statement.Operation == WriteOperation.Insert
            ? []
            : await ReadRowsAsync(connection, transaction, WriteSql.BeforeImageQuery(table, statement, Write.MaxRows + 1));

        if (beforeImage.Count > Write.MaxRows)
            return Fail(transaction, $"the statement matches more than {Write.MaxRows} rows (MCP_WRITE_MAX_ROWS). Narrow the WHERE clause.");

        int rows;
        string inverseSql;

        if (statement.Operation == WriteOperation.Insert)
        {
            var insertedKeys = await ReadRowsAsync(connection, transaction, WriteSql.InsertWithReturnedKeys(table, statement));
            rows = insertedKeys.Count;
            inverseSql = WriteSql.InverseOfInsert(table, insertedKeys);
        }
        else
        {
            var affected = await ExecuteNonQueryAsync(connection, transaction, statement.Sql);
            if (affected < beforeImage.Count)
                return Fail(transaction, $"the statement affected {affected} rows but its WHERE clause matched {beforeImage.Count}.");

            rows = beforeImage.Count;
            inverseSql = statement.Operation == WriteOperation.Delete
                ? WriteSql.InverseOfDelete(table, beforeImage)
                : WriteSql.InverseOfUpdate(table, targets, beforeImage);
        }

        if (rows == 0)
            return Fail(transaction, "the statement affects no rows.");

        if (rows > Write.MaxRows)
            return Fail(transaction, $"the statement affects {rows} rows, more than MCP_WRITE_MAX_ROWS ({Write.MaxRows}).");

        if (confirmedRows is not null && rows != confirmedRows)
            return Fail(transaction, $"the statement now affects {rows} rows instead of the {confirmedRows} rows that were prepared.", rows);

        if (confirmedRows is null)
            transaction.Rollback();
        else
            transaction.Commit();

        return new WriteExecution(null, table, rows, beforeImage, inverseSql);
    }

    private static string? ValidateSupportedTypes(WriteTable table, WriteStatement statement)
    {
        var unsupportedKey = table.KeyColumns.FirstOrDefault(column => !column.IsSupported);
        if (unsupportedKey is not null)
            return $"key column {unsupportedKey.Name} has type {unsupportedKey.TypeName}, which ExecuteWrite cannot use to revert the write.";

        var unsupportedInsertable = table.InsertableColumns.FirstOrDefault(column => !column.IsSupported);
        if (statement.Operation == WriteOperation.Delete && unsupportedInsertable is not null)
            return $"column {unsupportedInsertable.Name} has type {unsupportedInsertable.TypeName}, so deleted rows could not be restored. DELETE on {table.DisplayName} is rejected.";

        return null;
    }

    private static string? ResolveTargets(WriteTable table, WriteStatement statement, out IReadOnlyList<WriteColumn> targets)
    {
        var resolved = new List<WriteColumn>();
        targets = resolved;

        foreach (var targetName in statement.SetTargets)
        {
            var column = table.Columns.FirstOrDefault(candidate => string.Equals(candidate.Name, targetName, StringComparison.OrdinalIgnoreCase));

            if (column is null)
                return $"column {targetName} does not exist in {table.DisplayName}.";

            if (table.KeyColumns.Contains(column))
                return $"updating key column {column.Name} is not supported because the change could not be reverted by key ({table.KeyDescription}).";

            if (!column.IsInsertable)
                return $"{column.Name} is a computed or rowversion column and cannot be updated.";

            if (!column.IsSupported)
                return $"column {column.Name} has type {column.TypeName}, which ExecuteWrite cannot revert.";

            resolved.Add(column);
        }

        return null;
    }

    private static WriteExecution Fail(SqlTransaction transaction, string error, int rows = 0)
    {
        transaction.Rollback();
        return new WriteExecution(error, null, rows, [], string.Empty);
    }

    private async Task<int> ExecuteNonQueryAsync(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };
        return await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<string?[]>> ReadRowsAsync(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string?[]>();

        while (await reader.ReadAsync())
        {
            var row = new string?[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
                row[index] = reader.IsDBNull(index) ? null : reader.GetString(index);

            rows.Add(row);
        }

        return rows;
    }

    private void Audit(string auditEvent, string writeId, string database, WriteOperation operation, int rows) =>
        logger.LogInformation(
            "Write audit: event={Event} writeId={WriteId} database={Database} operation={Operation} rows={Rows} timestamp={Timestamp}",
            auditEvent, writeId, database, operation.ToString().ToUpperInvariant(), rows, timeProvider.GetUtcNow().ToString("O"));

    private string FormatPrepared(PendingWrite pending, WriteExecution execution)
    {
        var lines = new List<string> { "Write prepared. Nothing has been changed yet." };
        lines.AddRange(HeaderLines(pending, execution));
        lines.Add($"expires: {pending.ExpiresAt:O} (5 minutes)");
        lines.Add(string.Empty);
        lines.Add(Write.Confirmation == WriteConfirmationMode.Webhook
            ? "To apply it: a 6-digit confirmation code was sent to the configured confirmation channel. Ask the human for the code and call confirm_write with writeId, code and connectionId. After 3 wrong codes the writeId is invalidated."
            : "To apply it: call confirm_write with writeId and connectionId. The client will ask the human to approve the write.");
        lines.Add(string.Empty);
        lines.AddRange(BeforeImageLines(pending, execution));

        if (pending.Statement.Operation == WriteOperation.Insert)
        {
            lines.Add("Inverse SQL: generated by confirm_write from the keys of the rows actually inserted.");
        }
        else
        {
            lines.Add("Inverse SQL (preview from the current data; confirm_write returns the final version):");
            lines.Add(execution.InverseSql);
        }

        return string.Join('\n', lines);
    }

    private static string FormatCommitted(PendingWrite pending, WriteExecution execution)
    {
        var lines = new List<string> { "Write committed." };
        lines.AddRange(HeaderLines(pending, execution));
        lines.Add(string.Empty);
        lines.AddRange(BeforeImageLines(pending, execution));
        lines.Add("Inverse SQL to revert this write:");
        lines.Add(execution.InverseSql);

        return string.Join('\n', lines);
    }

    private static IEnumerable<string> HeaderLines(PendingWrite pending, WriteExecution execution) =>
    [
        $"writeId: {pending.WriteId}",
        $"operation: {pending.Statement.Operation.ToString().ToUpperInvariant()} · table: {pending.TableName} · target: {pending.Host}:{pending.Port}/{pending.Database} · rows: {execution.Rows}",
        $"key: {execution.Table!.KeyDescription}"
    ];

    private static IEnumerable<string> BeforeImageLines(PendingWrite pending, WriteExecution execution)
    {
        if (pending.Statement.Operation == WriteOperation.Insert)
            return [];

        return
        [
            $"Before image ({execution.BeforeImage.Count} row(s)):",
            string.Join(" | ", execution.Table!.ReadableColumns.Select(column => column.Name)),
            new string('-', 80),
            .. execution.BeforeImage.Select(row => string.Join(" | ", row.Select(value => value ?? "NULL"))),
            string.Empty
        ];
    }

    private sealed record WriteExecution(
        string? Error,
        WriteTable? Table,
        int Rows,
        IReadOnlyList<string?[]> BeforeImage,
        string InverseSql);
}
