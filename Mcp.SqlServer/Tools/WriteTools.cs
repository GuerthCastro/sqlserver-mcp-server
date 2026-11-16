using System.ComponentModel;
using Mcp.SqlServer.Services;
using ModelContextProtocol.Server;

namespace Mcp.SqlServer.Tools;

public class WriteTools(WriteService writeService)
{
    [McpServerTool, Description(
        "Prepares a single INSERT, UPDATE or DELETE against an allowed database. Nothing is changed: the statement runs inside a transaction that is rolled back, " +
        "and the response returns a writeId, the number of affected rows, the before image of the affected rows and the inverse SQL to revert the change. " +
        "UPDATE and DELETE require a WHERE clause; DDL, TRUNCATE, GRANT and multiple statements are rejected. " +
        "The write is applied only after a human confirms it with confirm_write within 5 minutes.")]
    public Task<string> ExecuteWrite(
        McpServer server,
        [Description("Database name")] string database,
        [Description("A single INSERT, UPDATE or DELETE statement")] string sql,
        [Description("connectionId returned by connect")] string? connectionId = null) =>
        writeService.PrepareAsync(server, connectionId, database, sql);

    [McpServerTool, Description(
        "Applies a write prepared by execute_write after human confirmation. With webhook confirmation, pass the 6-digit code the human received; " +
        "with elicitation, the client asks the human to approve. The statement is executed again and committed only if it affects the same number of rows; " +
        "the response returns the before image and the inverse SQL to revert the change.")]
    public Task<string> ConfirmWrite(
        McpServer server,
        [Description("writeId returned by execute_write")] string writeId,
        [Description("6-digit confirmation code given by the human (webhook confirmation only)")] string? code = null,
        [Description("connectionId used to prepare the write")] string? connectionId = null,
        CancellationToken cancellationToken = default) =>
        writeService.ConfirmAsync(server, writeId, code, connectionId, cancellationToken);
}
