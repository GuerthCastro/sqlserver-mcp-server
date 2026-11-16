# sqlserver-mcp-server

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![MCP SDK](https://img.shields.io/badge/MCP%20SDK-1.3.0-blue)](https://github.com/modelcontextprotocol/csharp-sdk)
[![Microsoft.Data.SqlClient](https://img.shields.io/badge/Microsoft.Data.SqlClient-7.1-CC2927?logo=microsoftsqlserver)](https://github.com/dotnet/SqlClient)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://hub.docker.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A **read-only** MCP server for SQL Server databases, built with .NET 10 and the official
[ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

Connect any MCP-compatible AI agent to your SQL Server instance and let it explore databases,
schemas, tables, and run read-only queries — all through natural language.

---

## Features

- Connect to any SQL Server instance (host, port, username, password)
- List all databases on the server
- List schemas within a database
- List tables within a schema
- Describe table structure: columns, types, nullability, primary keys, foreign keys
- Execute read-only `SELECT` / `WITH` queries inside a transaction that is always rolled back
- Per-client connections: each `Connect` returns its own `connectionId`, so clients never share credentials
- Bearer token authentication on the MCP endpoint
- Paged results with a row and character budget per page, plus a command timeout
- `/health` endpoint for Docker, Portainer or monitoring
- Lightweight Docker container with no persistent state
- Compatible with Claude Desktop, Claude Code, Cursor, Windsurf, and any MCP-compatible client

---

## Quick Start

```bash
git clone https://github.com/GuerthCastro/sqlserver-mcp-server.git
cd sqlserver-mcp-server
echo "MCP_API_TOKEN=$(openssl rand -hex 32)" > .env
docker compose up -d
```

The server starts on port `3200` by default and exposes the MCP endpoint at `/mcp`.

---

## MCP Tools

| Tool | Description |
|---|---|
| `Connect` | Connects to a SQL Server instance (host, port, username, password, optional default database) and returns a `connectionId` |
| `Disconnect` | Forgets the credentials of a `connectionId` |
| `ListDatabases` | Lists all user databases on the connected server |
| `ListSchemas` | Lists all user-defined schemas in a given database |
| `ListTables` | Lists all base tables in a given schema |
| `DescribeTable` | Describes columns, data types, nullability, primary keys, and foreign keys |
| `ExecuteQuery` | Executes a single read-only `SELECT` or `WITH` query and returns one page of results (see [Paging](#paging)) |

Every tool except `Connect` takes the `connectionId` returned by `Connect`. Idle connections
expire after 60 minutes by default.

---

## Paging

`ExecuteQuery` returns one page per call. A page starts at `offset` (default `0`) and ends at
the first of these limits:

- `MCP_PAGE_MAX_ROWS` rows (default `200`)
- `MCP_PAGE_MAX_CHARACTERS` characters of row data (default `20000`)

The first line of every response describes the page:

```
rows 201-400 · nextOffset: 400 · hasMore: true
```

- To read the next page, send the same SQL again with `offset` set to `nextOffset`.
- `offset > 0` requires an `ORDER BY` in the outer query, otherwise the query is rejected:
  without a stable order the database may return rows in a different order on each call and
  pages could overlap or skip rows. Order by a unique key.
- Every page re-executes the query and skips `offset` rows. For totals use `COUNT`, `SUM` or
  `GROUP BY` instead of reading every row. For very long walks prefer keyset pagination
  (`WHERE id > last_id ORDER BY id`) with `offset` 0.
- A row longer than `MCP_PAGE_MAX_CHARACTERS` is returned alone, cut at the limit and marked
  `[ROW TRUNCATED: showing N of M characters]`. `nextOffset` still moves past it.
- Pages are not a consistent snapshot. If the data changes between calls, rows can be repeated
  or missed.
- The SQL is never rewritten. The server stops reading once the page is full and cancels the
  rest of the result.

---

## Configuration

Settings are read from environment variables. With Docker Compose, put them in a `.env` file
next to `docker-compose.yml` (the file is git-ignored).

| Variable | Required | Default | Description |
|---|---|---|---|
| `MCP_API_TOKEN` | Yes | | Bearer token clients must send. Generate with `openssl rand -hex 32` |
| `MCP_COMMAND_TIMEOUT_SECONDS` | No | `30` | Timeout for every database command |
| `MCP_PAGE_MAX_ROWS` | No | `200` | Maximum rows per `ExecuteQuery` page |
| `MCP_PAGE_MAX_CHARACTERS` | No | `20000` | Maximum characters of row data per `ExecuteQuery` page |
| `MCP_CONNECTION_IDLE_MINUTES` | No | `60` | Minutes before an unused `connectionId` expires |

Database credentials are not configured here: they are sent at runtime through `Connect` and
kept in memory only. To change the exposed port, edit the `ports` mapping in `docker-compose.yml`.

---

## Client Setup

### Claude Desktop

Add the following to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "sqlserverServer": {
      "type": "http",
      "url": "http://localhost:3200/mcp",
      "headers": {
        "Authorization": "Bearer YOUR_MCP_API_TOKEN"
      }
    }
  }
}
```

### Claude Code

```bash
claude mcp add --transport http sqlserverServer http://localhost:3200/mcp \
  --header "Authorization: Bearer YOUR_MCP_API_TOKEN"
```

### Generic MCP Client

Point your client to the HTTP endpoint and send the header `Authorization: Bearer YOUR_MCP_API_TOKEN`:

```
http://localhost:3200/mcp
```

---

## Example Conversation

```
You: Connect to my database at 192.168.1.10, user readonly_user, password secret

Claude: Connected to SQL Server 2022 at 192.168.1.10:1433 (connectionId: 3f9c...)

You: What databases are available?

Claude: Found 3 databases:
- myapp
- analytics
- inventory

You: Show me the schemas in myapp

Claude: Found 2 schemas in myapp:
- audit
- dbo

You: List the tables in dbo

Claude: Found 4 tables in dbo:
- orders
- products
- users
- categories

You: Describe the users table

Claude: Table: dbo.users
--------------------------------------------------------------------------------
Column                         Type                 Nullable   PK    FK    References
--------------------------------------------------------------------------------
id                             bigint               NO         YES   NO
email                          nvarchar(255)        NO         NO    NO
name                           nvarchar(255)        YES        NO    NO
role_id                        int                  YES        NO    YES   roles.id
created_at                     datetime2            NO         NO    NO

You: How many users were created in the last 30 days?

Claude: SELECT COUNT(*) FROM users WHERE created_at >= DATEADD(day, -30, GETUTCDATE())

rows 1-1 · nextOffset: 1 · hasMore: false
count
--------
142
```

---

## Security

- **Read-only by design.** A keyword guard rejects multiple statements, `SELECT ... INTO`,
  `EXEC`, `MERGE`, `OPENROWSET`, DML/DDL keywords and other statements with side effects before
  they reach the database. Every query also runs inside a transaction that is always rolled back.
- **Use a read-only login.** SQL Server has no read-only transaction mode, so the strongest
  guarantee is a login that only has the `db_datareader` role. Do not connect as `sa`.
- **Authenticated endpoint.** Requests to `/mcp` require `Authorization: Bearer <MCP_API_TOKEN>`.
  Only `/health` is public.
- **Isolated connections.** Each client gets its own `connectionId`; credentials are held in
  memory only, never written to disk or logs, and expire when idle.
- **Bounded output.** Results are paged by row and character budgets and every command has a
  timeout. The server stops reading once a page is full and cancels the rest of the result
  instead of loading every row.
- **Local network recommended.** Do not expose the port to the internet without TLS and an
  additional authentication layer.

---

## Requirements

- [Docker](https://www.docker.com/products/docker-desktop) (for container deployment)
- Any MCP-compatible client (Claude Desktop, Claude Code, Cursor, Windsurf, etc.)
- A running SQL Server instance accessible from the container

### Building from source

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
cd Mcp.SqlServer
dotnet run
```

The server starts on `http://localhost:5000` by default when running locally.

---

## Architecture

```
sqlserver-mcp-server/
└── Mcp.SqlServer/
    ├── Configuration/
    │   ├── RuntimeOptions.cs        # Server settings read from environment variables
    │   └── ServerConfiguration.cs   # Immutable record holding connection parameters
    ├── Services/
    │   ├── DatabaseService.cs       # Per-connectionId registry of connection configs
    │   ├── QueryGuard.cs            # Pre-validation of ExecuteQuery input and paging
    │   ├── ResultPager.cs           # Reads one page of an ExecuteQuery result
    │   └── SqlServerQueries.cs      # SQL constants and security policy
    ├── Tools/
    │   ├── ConnectTool.cs           # MCP tools: connect, disconnect
    │   ├── ListDatabasesTool.cs     # MCP tool: listDatabases
    │   ├── ListSchemasTool.cs       # MCP tool: listSchemas
    │   ├── ListTablesTool.cs        # MCP tool: listTables
    │   ├── DescribeTableTool.cs     # MCP tool: describeTable
    │   └── ExecuteQueryTool.cs      # MCP tool: executeQuery
    └── Program.cs                   # ASP.NET Core bootstrap
```

The `DatabaseService` keeps one configuration per `connectionId`, so concurrent clients are
isolated from each other. The server uses
[`ModelContextProtocol.AspNetCore`](https://github.com/modelcontextprotocol/csharp-sdk)
with HTTP transport.

---

## Stack

| Component | Technology |
|---|---|
| Runtime | [.NET 10](https://dotnet.microsoft.com) |
| MCP SDK | [ModelContextProtocol.AspNetCore 1.3.0](https://github.com/modelcontextprotocol/csharp-sdk) |
| SQL Server driver | [Microsoft.Data.SqlClient 6.0](https://github.com/dotnet/SqlClient) |
| Container | [Docker](https://www.docker.com) |

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting
a pull request.

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a history of changes.

---

## License

This project is licensed under the [MIT License](LICENSE).

---

*Built by [Guerth Castro](https://github.com/GuerthCastro)*
