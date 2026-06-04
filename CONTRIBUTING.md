# Contributing to sqlserver-mcp-server

Thank you for considering a contribution. This document covers how to set up the project,
the conventions used, and how to submit a pull request.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/products/docker-desktop) (optional, for container testing)
- A running SQL Server instance for manual testing

### Clone and build

```bash
git clone https://github.com/GuerthCastro/sqlserver-mcp-server.git
cd sqlserver-mcp-server
dotnet build
```

### Run locally

```bash
cd Mcp.SqlServer
dotnet run
```

The MCP endpoint will be available at `http://localhost:5000/mcp`.

### Run with Docker

```bash
docker compose up --build
```

The MCP endpoint will be available at `http://localhost:3100/mcp`.

---

## Project Structure

```
sqlserver-mcp-server/
└── Mcp.SqlServer/
    ├── Configuration/   # Immutable configuration records
    ├── Services/        # Singleton services and SQL constants
    ├── Tools/           # One file per MCP tool
    └── Program.cs       # ASP.NET Core entry point
```

Each MCP tool lives in its own file under `Tools/`. New tools follow the same pattern:
a class annotated with `[McpServerToolType]` and a single public async method annotated
with `[McpServerTool]`.

---

## Conventions

- **Language**: All code, comments, commits, and documentation must be written in English.
- **Framework**: .NET 10, C# with file-scoped namespaces and primary constructors.
- **Style**: Follow the existing code style — no reformatting of unrelated code.
- **SQL**: All SQL lives in `SqlServerQueries.cs` as string constants. No inline SQL in tool files.
- **Security**: Any new `ExecuteQuery`-style tool must enforce the same keyword guard and
  output truncation as the existing implementation.
- **No EF Core**: This project uses Microsoft.Data.SqlClient directly. Do not introduce Entity Framework.
- **XML docs**: All public types and members must have `<summary>` documentation.

---

## Submitting a Pull Request

1. Fork the repository and create a branch from `main`:
   ```bash
   git checkout -b feature/my-feature
   ```

2. Make your changes. Keep commits focused and atomic.

3. Verify the project builds cleanly:
   ```bash
   dotnet build --configuration Release
   ```

4. Verify the Docker build succeeds:
   ```bash
   docker build -t sqlserver-mcp-server-test .
   ```

5. Open a pull request against `main` with a clear description of what the change does
   and why. Reference any related issues.

---

## Reporting Issues

Use [GitHub Issues](https://github.com/GuerthCastro/sqlserver-mcp-server/issues) to report
bugs or request features. Include:

- A clear description of the problem or request.
- Steps to reproduce (for bugs).
- SQL Server version and client used (for bugs).
- Expected vs. actual behavior (for bugs).

---

## License

By contributing, you agree that your contributions will be licensed under the
[MIT License](LICENSE).
