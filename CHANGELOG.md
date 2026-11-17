# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.4.0] - 2026-11-16

### Removed

- `MCP_WRITE_ALLOWED_TARGETS` and the per-target write check. `ExecuteWrite` is allowed on any database the connection can reach, and protection relies on the human confirmation and the privileges of the database user. The variable is ignored if it is still set.
- The allowed targets count from the startup log.

---

## [0.3.0] - 2026-11-16

### Added

- Optional writes: `ExecuteWrite` and `ConfirmWrite` MCP tools, registered only with `MCP_WRITE_MODE=on` and a confirmation method (`MCP_WRITE_CONFIRMATION=elicitation|webhook`; `none` keeps writes disabled).
- `ExecuteWrite` accepts a single `INSERT`, `UPDATE` or `DELETE` on one table. `UPDATE`/`DELETE` without `WHERE`, DDL, `TRUNCATE`, `DROP`, `GRANT`, login administration and multiple statements are always rejected.
- Writes are limited to the `host:port/database` targets in `MCP_WRITE_ALLOWED_TARGETS` and to `MCP_WRITE_MAX_ROWS` rows (default 50).
- `ExecuteWrite` runs the statement in a `SERIALIZABLE` transaction, captures the before image with the same `WHERE`, counts the rows and rolls back. It returns a `writeId`, the row count, the before image and the inverse SQL.
- `ConfirmWrite` re-executes the statement and commits only if it affects the same number of rows; otherwise it rolls back and aborts. It returns the before image and the inverse SQL.
- Inverse SQL identifies rows by primary key or, if there is none, by a unique index on `NOT NULL` columns. Tables without a reliable key are rejected.
- `INSERT` is rewritten to capture the inserted keys: `OUTPUT INSERTED.<key columns> INTO @mcp_inserted_keys` is inserted before `VALUES`, `SELECT` or `DEFAULT VALUES` (`OUTPUT` with `INTO` also works on tables with triggers). Statements that already contain `OUTPUT` are rejected.
- Confirmation by MCP elicitation (fails closed if the client does not support it) or by a 6-digit code sent only to a webhook signed with HMAC-SHA256 (`X-Signature`, `X-Timestamp`), configured with `MCP_WRITE_CONFIRM_WEBHOOK_URL` and `MCP_WRITE_CONFIRM_WEBHOOK_SECRET`. The code never appears in tool responses or logs.
- A `writeId` expires after 5 minutes, is single use, is bound to the SQL hash, the `connectionId` and the database, and is invalidated after 3 wrong codes.
- Audit log entries for every write event (timestamp, database, operation, rows, `writeId`), without credentials, full SQL or codes.
- Unit tests for the write guard, inverse SQL, pending writes, write configuration and webhook signature.

### Changed

- Target .NET 11 (`net11.0`) and build the Docker image on `mcr.microsoft.com/dotnet/sdk:11.0` and `mcr.microsoft.com/dotnet/aspnet:11.0`.
- The SQL lexer used by the read-only guard (comments, quoted text, top-level clauses) moved to `SqlText` and is shared with the write guard. Behavior is unchanged.

---

## [0.2.0] - 2026-11-16

### Added

- Bearer token authentication on `/mcp` through the required `MCP_API_TOKEN` environment variable.
- `/health` endpoint (no authentication) for container and monitoring checks.
- `Disconnect` MCP tool.
- Configurable limits: `MCP_COMMAND_TIMEOUT_SECONDS`, `MCP_PAGE_MAX_ROWS` (default 200), `MCP_PAGE_MAX_CHARACTERS` (default 20000), `MCP_CONNECTION_IDLE_MINUTES`.
- `ExecuteQuery` paging: optional `offset` parameter, and every response starts with `rows X-Y · nextOffset: N · hasMore: true|false`. `offset > 0` requires an `ORDER BY` in the outer query. A row larger than `MCP_PAGE_MAX_CHARACTERS` is returned truncated with a marker and `nextOffset` moves past it. Each page re-executes the query, so pages are not consistent if the data changes between calls.
- `ExecuteQuery` accepts queries starting with `WITH` (CTEs).
- `Mcp.SqlServer.Tests` project with unit tests for the query guard.

### Changed

- **Breaking:** `Connect` returns a `connectionId` and every other tool requires it. Connections are isolated per client instead of being shared by all clients of the process.
- Calling a tool without `connectionId` returns the same "Unknown or expired connectionId" message as an invalid one, instead of a generic SDK error.
- `ExecuteQuery` runs inside a transaction that is always rolled back.
- `ExecuteQuery` stops reading once the page is full and cancels the rest of the result set. The SQL is never rewritten for paging.
- Keyword guard ignores string literals and quoted identifiers, rejects multiple statements, and blocks `INTO`, `EXEC`, `MERGE`, `OPENROWSET`, `DBCC`, `WAITFOR` and other statements with side effects.
- Keyword guard removes `--` and `/* */` comments before validating; the query sent to the server is the same text without comments.
- Failures while cancelling a truncated query, closing its reader or rolling back are logged instead of being ignored (exception type and message only, no SQL or credentials). The expected cancellation error after truncating a result is logged as debug; anything else as a warning.
- `Microsoft.Data.SqlClient` upgraded from 6.0.2 to 7.1.2.

### Security

- Fixed: any client could run queries with the credentials of the last client that called `Connect`.
- Fixed: `SELECT ... INTO` could create tables and `SELECT 1; EXEC ...` could run procedures despite the read-only guard.
- `System.Security.Cryptography.Pkcs` resolves to 9.0.21 through the `Microsoft.Data.SqlClient` upgrade, removing NU1902 / NU1903 (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx).

### Fixed

- Compose service renamed from `postgres-mcp` to `sqlserver-mcp`.
- README listed port `3100`; this server uses `3200`.

---

## [0.1.0] - 2025-05-01

### Added

- `Connect` MCP tool — connects to a SQL Server instance by host, port, username, password, and optional default database.
- `ListDatabases` MCP tool — lists all user databases on the connected server.
- `ListSchemas` MCP tool — lists all user-defined schemas in a given database.
- `ListTables` MCP tool — lists all base tables in a given schema.
- `DescribeTable` MCP tool — describes column names, data types, nullability, primary keys, and foreign key references.
- `ExecuteQuery` MCP tool — executes read-only `SELECT` queries with output truncation at 50,000 characters.
- Keyword-based SQL guard: rejects queries containing `INSERT`, `UPDATE`, `DELETE`, `DROP`, `TRUNCATE`, `ALTER`, `CREATE`, `GRANT`, or `REVOKE`.
- `DatabaseService` singleton managing in-memory connection configuration.
- `SqlServerQueries` static class centralizing all SQL constants and security policy constants.
- `ServerConfiguration` immutable record holding resolved connection parameters.
- ASP.NET Core bootstrap with MCP HTTP transport exposed at `/mcp`.
- Docker multi-stage build targeting `mcr.microsoft.com/dotnet/aspnet:10.0`.
- `docker-compose.yml` exposing port `3100`.

[Unreleased]: https://github.com/GuerthCastro/sqlserver-mcp-server/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/GuerthCastro/sqlserver-mcp-server/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/GuerthCastro/sqlserver-mcp-server/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/GuerthCastro/sqlserver-mcp-server/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/GuerthCastro/sqlserver-mcp-server/releases/tag/v0.1.0
