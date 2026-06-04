namespace Mcp.SqlServer.Configuration;

public class ServerConfiguration
{
    public string Host { get; init; } = string.Empty;
    public int? Port { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? Database { get; init; }
    public int ResolvedPort => Port ?? 1433;
    public bool IsConnected => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username);
}
