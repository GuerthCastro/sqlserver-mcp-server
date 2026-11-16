namespace Mcp.SqlServer.Configuration;

public sealed class RuntimeOptions
{
    public required string ApiToken { get; init; }
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int PageMaxRows { get; init; } = 200;
    public int PageMaxCharacters { get; init; } = 20_000;
    public TimeSpan ConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(60);
    public WriteOptions Write { get; init; } = WriteOptions.Disabled;

    public static RuntimeOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static RuntimeOptions From(Func<string, string?> readVariable)
    {
        var token = readVariable("MCP_API_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "MCP_API_TOKEN environment variable is required. Generate one with: openssl rand -hex 32");

        return new RuntimeOptions
        {
            ApiToken = token.Trim(),
            CommandTimeoutSeconds = ReadInt(readVariable, "MCP_COMMAND_TIMEOUT_SECONDS", 30),
            PageMaxRows = ReadInt(readVariable, "MCP_PAGE_MAX_ROWS", 200),
            PageMaxCharacters = ReadInt(readVariable, "MCP_PAGE_MAX_CHARACTERS", 20_000),
            ConnectionIdleTimeout = TimeSpan.FromMinutes(ReadInt(readVariable, "MCP_CONNECTION_IDLE_MINUTES", 60)),
            Write = WriteOptions.From(readVariable)
        };
    }

    private static int ReadInt(Func<string, string?> readVariable, string name, int defaultValue)
    {
        var raw = readVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }
}
