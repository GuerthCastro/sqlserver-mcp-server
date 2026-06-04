# sqlserver-mcp-server

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![MCP SDK](https://img.shields.io/badge/MCP%20SDK-1.3.0-blue)](https://github.com/modelcontextprotocol/csharp-sdk)
[![Microsoft.Data.SqlClient](https://img.shields.io/badge/Microsoft.Data.SqlClient-6.0-CC2927?logo=microsoftsqlserver)](https://github.com/dotnet/SqlClient)
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
- Execute read-only `SELECT` queries with output truncation
- Rejects non-`SELECT` statements and forbidden keywords (`INSERT`, `UPDATE`, `DELETE`, `DROP`, etc.)
- Lightweight Docker container — no persistent state, no configuration files
- Compatible with Claude Desktop, Claude Code, Cursor, Windsurf, and any MCP-compatible client

---

## Quick Start

```bash
git clone https://github.com/GuerthCastro/sqlserver-mcp-server.git
cd sqlserver-mcp-server
docker compose up -d
```

The server starts on port `3100` by default and exposes the MCP endpoint at `/mcp`.

---

## MCP Tools

| Tool | Description |
|---|---|
| `Connect` | Connects to a SQL Server instance (host, port, username, password, optional default database) |
| `ListDatabases` | Lists all user databases on the connected server |
| `ListSchemas` | Lists all user-defined schemas in a given database |
| `ListTables` | Lists all base tables in a given schema |
| `DescribeTable` | Describes columns, data types, nullability, primary keys, and foreign keys |
| `ExecuteQuery` | Executes a read-only `SELECT` query and returns results as formatted text |

---

## Configuration

No configuration is required at the container level. The connection is established at runtime
via the `Connect` tool and held in memory for the lifetime of the process.

To change the exposed port, edit `docker-compose.yml`:

```yaml
services:
  sqlserver-mcp:
    build: .
    ports:
      - "YOUR_PORT:80"
    restart: unless-stopped
```

---

## Client Setup

### Claude Desktop

Add the following to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "SqlServerServer": {
      "url": "http://localhost:3100/mcp"
    }
  }
}
```

### Claude Code

```bash
claude mcp add SqlServerServer http://localhost:3100/mcp
```

### Generic MCP Client

Point your client to the SSE or HTTP endpoint:

```
http://localhost:3100/mcp
```

---

## Example Conversation

```
You: Connect to my database at 192.168.1.10, user sa, password secret

Claude: Connected to SQL Server 2022 at 192.168.1.10:1433

You: What databases are available?

Claude: Found 3 databases:
- myapp
- analytics
- master

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

 count
--------
 142

1 row(s) returned.
```

---

## Security

- **Read-only by design.** Only `SELECT` queries are accepted. Any statement containing
  `INSERT`, `UPDATE`, `DELETE`, `DROP`, `TRUNCATE`, `ALTER`, `CREATE`, `GRANT`, or `REVOKE`
  is rejected before reaching the database.
- **No credentials stored.** Connection parameters are held in memory only for the lifetime
  of the running process and are never written to disk or logs.
- **Output is bounded.** Query results are truncated at 50,000 characters to prevent
  unbounded memory usage.
- **Local network recommended.** Deploy on your internal network. Do not expose the container
  port to the public internet without additional authentication (e.g., a reverse proxy with TLS
  and auth).

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
    │   └── ServerConfiguration.cs   # Immutable record holding connection parameters
    ├── Services/
    │   ├── DatabaseService.cs       # Singleton managing the active connection config
    │   └── SqlServerQueries.cs     # SQL constants and security policy
    ├── Tools/
    │   ├── ConnectTool.cs           # MCP tool: connect
    │   ├── ListDatabasesTool.cs     # MCP tool: listDatabases
    │   ├── ListSchemasTool.cs       # MCP tool: listSchemas
    │   ├── ListTablesTool.cs        # MCP tool: listTables
    │   ├── DescribeTableTool.cs     # MCP tool: describeTable
    │   └── ExecuteQueryTool.cs      # MCP tool: executeQuery
    └── Program.cs                   # ASP.NET Core bootstrap
```

The `DatabaseService` is registered as a singleton so all MCP tool invocations within a
session share the same connection configuration. The server uses
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
