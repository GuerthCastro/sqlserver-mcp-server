# sqlserver-mcp-server

[![.NET](https://img.shields.io/badge/.NET-11.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![MCP SDK](https://img.shields.io/badge/MCP%20SDK-1.3.0-blue)](https://github.com/modelcontextprotocol/csharp-sdk)
[![Microsoft.Data.SqlClient](https://img.shields.io/badge/Microsoft.Data.SqlClient-7.1-CC2927?logo=microsoftsqlserver)](https://github.com/dotnet/SqlClient)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://hub.docker.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A **read-only by default** MCP server for SQL Server databases, built with .NET 11 and the official
[ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

Connect any MCP-compatible AI agent to your SQL Server instance and let it explore databases,
schemas, tables, and run read-only queries — all through natural language. Writes are optional,
off by default, and always confirmed by a human.

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
- Optional writes (`INSERT`, `UPDATE`, `DELETE`) with an allow list, a row limit, human confirmation
  through MCP elicitation or a signed webhook, before image and inverse SQL
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

`ExecuteWrite` and `ConfirmWrite` are registered only when writes are enabled (see [Writes](#writes-optional)).

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

## Writes (optional)

Writes are **off by default**. With `MCP_WRITE_MODE=on` and a confirmation method configured,
two extra tools are registered. With writes off, they do not exist for the client.

| Tool | Description |
|---|---|
| `ExecuteWrite` | Prepares a single `INSERT`, `UPDATE` or `DELETE`. Nothing is changed: the statement runs in a transaction that is rolled back, and the response returns a `writeId`, the affected rows, the before image and the inverse SQL |
| `ConfirmWrite` | Applies a prepared write after a human confirms it, and returns the before image and the inverse SQL to revert it |

### How a write flows

1. The agent calls `ExecuteWrite(connectionId, database, sql)`.
2. The server validates the statement. It then runs the statement inside a `SERIALIZABLE` transaction,
   captures the before image (a `SELECT` with the same `WHERE`), counts the rows and **rolls back**.
3. A human confirms the write, either in the MCP client (elicitation) or by reading a 6-digit code
   that is delivered only to the confirmation webhook.
4. The agent calls `ConfirmWrite(writeId, code, connectionId)`. The statement runs again. If it
   affects a different number of rows than prepared, it is rolled back and aborted. Otherwise it is
   committed, and the response includes the inverse SQL to revert it.

### Rules

- Only a single `INSERT`, `UPDATE` or `DELETE` on one table of the target database.
  `UPDATE` and `DELETE` without `WHERE` are rejected. DDL, `TRUNCATE`, `DROP`, `GRANT`,
  login and user administration and multiple statements are always rejected.
- Not supported: `OUTPUT`, `TOP`, `UPDATE ... FROM`, `DELETE ... FROM ... JOIN`, table hints, table
  aliases, variables in `SET`, `.WRITE` and tables in another database.
- At most `MCP_WRITE_MAX_ROWS` rows per write (default `50`).
- Writes are allowed on any database the connection can reach. Protection relies on the human
  confirmation and the privileges of the database user.
- The table needs a primary key, or a unique index on `NOT NULL` columns. That key identifies the
  rows in the inverse SQL. Tables without a reliable key are rejected, and key columns cannot be
  updated.
- Columns of type `sql_variant`, `geography`, `geometry`, `hierarchyid` or user-defined types cannot be
  used as keys or updated, and `DELETE` is rejected on tables that have them. Computed and
  `rowversion` columns are skipped when rows are restored.
- A `writeId` expires after 5 minutes, can be used once, and is bound to the hash of the SQL, the
  `connectionId` and the database. Three wrong codes invalidate it.
- **The `INSERT` statement is rewritten.** To build the inverse `DELETE`, the server needs the keys of
  the rows that were actually inserted, so it inserts
  `OUTPUT INSERTED.<key columns> INTO @mcp_inserted_keys` before `VALUES`, `SELECT` or `DEFAULT VALUES`.
  `OUTPUT ... INTO` is used because SQL Server rejects `OUTPUT` without `INTO` on tables with enabled
  triggers. Statements that already contain `OUTPUT` are rejected. The inverse SQL for an
  `INSERT` is returned by `ConfirmWrite`, because generated keys only exist after the real insert.
- The inverse SQL restores the before image captured at commit time. It does not undo trigger side
  effects, and it does not rewind identity or sequence values.

### Write configuration

| Variable | Default | Description |
|---|---|---|
| `MCP_WRITE_MODE` | `off` | `on` registers `ExecuteWrite` and `ConfirmWrite`, if a confirmation method is set |
| `MCP_WRITE_CONFIRMATION` | `none` | `elicitation`, `webhook` or `none`. `none` disables writes even with `MCP_WRITE_MODE=on` |
| `MCP_WRITE_MAX_ROWS` | `50` | Maximum rows a single write may affect |
| `MCP_WRITE_CONFIRM_WEBHOOK_URL` | | Required for `webhook`. Must be `https` unless it points to localhost |
| `MCP_WRITE_CONFIRM_WEBHOOK_SECRET` | | Required for `webhook`. At least 32 characters. Generate with `openssl rand -hex 32` |

The server fails at startup when the write configuration is invalid.

With `elicitation`, the client must support MCP elicitation. If it does not, `ExecuteWrite` and
`ConfirmWrite` fail closed and nothing is written.

### Confirmation webhook

For every prepared write, the server sends one `POST` with a JSON body:

```json
{
  "event": "write.confirmation_requested",
  "server": "sqlserver-mcp-server",
  "writeId": "9f2c4d1e8a7b6c5d4e3f2a1b0c9d8e7f",
  "code": "482915",
  "timestamp": 1790000000,
  "expiresAt": "2026-09-21T14:05:00.0000000+00:00",
  "target": { "host": "db.example.internal", "port": 1433, "database": "app" },
  "operation": "UPDATE",
  "table": "dbo.orders",
  "rows": 3,
  "sql": "UPDATE orders SET status = 'cancelled' WHERE id IN (101, 102, 103)"
}
```

Headers:

| Header | Value |
|---|---|
| `X-Timestamp` | Unix time in seconds; also present in the body |
| `X-Signature` | `sha256=` followed by the lowercase hex HMAC-SHA256 of `{X-Timestamp}.{raw body}`, keyed with `MCP_WRITE_CONFIRM_WEBHOOK_SECRET` |

To verify a request, the receiver should:

1. Read the raw body before parsing it.
2. Compute the HMAC-SHA256 of `{X-Timestamp}.{raw body}` with the shared secret and compare it
   with `X-Signature` in constant time.
3. Reject timestamps older or newer than 5 minutes, and `writeId` values it has already seen
   (replay protection).
4. Deliver the code to the human, and answer `2xx` only after that. Any other status, or no
   answer within 10 seconds, makes `ExecuteWrite` fail and nothing is prepared.

The code appears only in the webhook body. It is never included in tool responses or in the
server logs.

Minimal receiver, Python standard library only. `deliver_to_human` is a placeholder for your
notification channel:

```python
import hashlib, hmac, json, os, time
from http.server import BaseHTTPRequestHandler, HTTPServer

SECRET = os.environ["MCP_WRITE_CONFIRM_WEBHOOK_SECRET"].encode()
seen_write_ids = set()

def deliver_to_human(message: str) -> None:
    raise NotImplementedError("Send the message through a channel only the human can read")

class ConfirmationReceiver(BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers["Content-Length"]))
        timestamp = self.headers.get("X-Timestamp", "")
        expected = "sha256=" + hmac.new(SECRET, timestamp.encode() + b"." + body, hashlib.sha256).hexdigest()
        if not hmac.compare_digest(expected, self.headers.get("X-Signature", "")):
            return self.reply(401)
        if not timestamp.isdigit() or abs(time.time() - int(timestamp)) > 300:
            return self.reply(401)
        payload = json.loads(body)
        if payload["writeId"] in seen_write_ids:
            return self.reply(409)
        seen_write_ids.add(payload["writeId"])
        deliver_to_human(f"{payload['operation']} on {payload['table']} ({payload['rows']} rows)\n"
                         f"{payload['sql']}\nConfirmation code: {payload['code']}")
        self.reply(204)

    def reply(self, status: int) -> None:
        self.send_response(status)
        self.end_headers()

HTTPServer(("0.0.0.0", 8080), ConfirmationReceiver).serve_forever()
```

> **Warning.** The confirmation channel must not be readable by the AI agent. If the agent can
> read the mailbox, chat, log files or notification feed where the code arrives, for example
> through another MCP server or a connected integration, it can confirm its own writes and the
> human approval step is gone. Use a channel that only the human can see, and do not log the code
> in the receiver.

### Audit log

Every write event is logged at `Information` level with the timestamp, database, operation, row
count and `writeId`: `prepared`, `committed`, `aborted`, `failed`, `cancelled`, `invalid_code` and
`invalidated`. Credentials, the full SQL and the confirmation code are never logged.

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
| `MCP_WRITE_*` | No | | Optional writes, off by default. See [Write configuration](#write-configuration) |

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

- **`ExecuteQuery` is read-only.** A keyword guard rejects multiple statements, `SELECT ... INTO`,
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
- **Writes are opt-in and human-confirmed.** Write tools exist only with `MCP_WRITE_MODE=on`, and every write needs a human confirmation through
  a channel the agent cannot read. Use a dedicated database user with the minimum write privileges.
- **Local network recommended.** Do not expose the port to the internet without TLS and an
  additional authentication layer.

---

## Requirements

- [Docker](https://www.docker.com/products/docker-desktop) (for container deployment)
- Any MCP-compatible client (Claude Desktop, Claude Code, Cursor, Windsurf, etc.)
- A running SQL Server instance accessible from the container

### Building from source

- [.NET 11 SDK](https://dotnet.microsoft.com/download)

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
    │   ├── WriteOptions.cs          # Write settings
    │   └── ServerConfiguration.cs   # Immutable record holding connection parameters
    ├── Services/
    │   ├── DatabaseService.cs       # Per-connectionId registry of connection configs
    │   ├── QueryGuard.cs            # Pre-validation of ExecuteQuery input and paging
    │   ├── ResultPager.cs           # Reads one page of an ExecuteQuery result
    │   ├── SqlText.cs               # Comment, quoting and top-level SQL lexer
    │   ├── WriteGuard.cs            # Validation and parsing of ExecuteWrite statements
    │   ├── WriteCatalog.cs          # Table, column and unique key lookup for writes
    │   ├── WriteSql.cs              # Before image, key capture and inverse SQL
    │   ├── WriteService.cs          # Prepare, confirm, commit and audit of writes
    │   ├── PendingWriteStore.cs     # writeId registry: expiry, binding and codes
    │   ├── ConfirmationWebhook.cs   # Signed confirmation webhook
    │   └── SqlServerQueries.cs      # SQL constants and security policy
    ├── Tools/
    │   ├── ConnectTool.cs           # MCP tools: connect, disconnect
    │   ├── ListDatabasesTool.cs     # MCP tool: listDatabases
    │   ├── ListSchemasTool.cs       # MCP tool: listSchemas
    │   ├── ListTablesTool.cs        # MCP tool: listTables
    │   ├── DescribeTableTool.cs     # MCP tool: describeTable
    │   ├── ExecuteQueryTool.cs      # MCP tool: executeQuery
    │   └── WriteTools.cs            # MCP tools: executeWrite, confirmWrite (writes enabled only)
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
| Runtime | [.NET 11](https://dotnet.microsoft.com) |
| MCP SDK | [ModelContextProtocol.AspNetCore 1.3.0](https://github.com/modelcontextprotocol/csharp-sdk) |
| SQL Server driver | [Microsoft.Data.SqlClient 7.1.2](https://github.com/dotnet/SqlClient) |
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
