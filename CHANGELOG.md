# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

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

[Unreleased]: https://github.com/GuerthCastro/sqlserver-mcp-server/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/GuerthCastro/sqlserver-mcp-server/releases/tag/v0.1.0
