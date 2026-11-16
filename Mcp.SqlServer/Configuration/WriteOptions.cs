using System.Net;

namespace Mcp.SqlServer.Configuration;

public enum WriteConfirmationMode
{
    None,
    Elicitation,
    Webhook
}

public sealed record WriteTarget(string Host, int Port, string Database)
{
    public static WriteTarget Parse(string value)
    {
        var trimmed = value.Trim();
        var slash = trimmed.IndexOf('/');
        var colon = slash < 0 ? -1 : trimmed.LastIndexOf(':', slash);

        if (slash < 0 || colon <= 0 || slash == trimmed.Length - 1)
            throw new InvalidOperationException($"Invalid MCP_WRITE_ALLOWED_TARGETS entry '{trimmed}'. Expected host:port/database.");

        var host = trimmed[..colon];
        var portText = trimmed[(colon + 1)..slash];
        var database = trimmed[(slash + 1)..];

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException($"Invalid port in MCP_WRITE_ALLOWED_TARGETS entry '{trimmed}'.");

        return new WriteTarget(host, port, database);
    }

    public bool Matches(string host, int port, string database) =>
        string.Equals(Host, host, StringComparison.OrdinalIgnoreCase)
        && Port == port
        && string.Equals(Database, database, StringComparison.OrdinalIgnoreCase);
}

public sealed class WriteOptions
{
    public const int MinimumWebhookSecretLength = 32;

    public static readonly WriteOptions Disabled = new();

    public bool ModeOn { get; init; }
    public WriteConfirmationMode Confirmation { get; init; } = WriteConfirmationMode.None;
    public IReadOnlyList<WriteTarget> AllowedTargets { get; init; } = [];
    public int MaxRows { get; init; } = 50;
    public Uri? WebhookUrl { get; init; }
    public string WebhookSecret { get; init; } = string.Empty;

    public bool Enabled => ModeOn && Confirmation != WriteConfirmationMode.None;

    public bool IsAllowed(string host, int port, string database) =>
        AllowedTargets.Any(target => target.Matches(host, port, database));

    public static WriteOptions From(Func<string, string?> readVariable)
    {
        var mode = (readVariable("MCP_WRITE_MODE") ?? "off").Trim().ToLowerInvariant();

        if (mode is not ("off" or "on"))
            throw new InvalidOperationException("MCP_WRITE_MODE must be 'off' or 'on'.");

        if (mode == "off")
            return Disabled;

        var confirmation = ParseConfirmation(readVariable("MCP_WRITE_CONFIRMATION"));

        if (confirmation == WriteConfirmationMode.None)
            return new WriteOptions { ModeOn = true, Confirmation = WriteConfirmationMode.None };

        var targets = (readVariable("MCP_WRITE_ALLOWED_TARGETS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(WriteTarget.Parse)
            .ToList();

        if (targets.Count == 0)
            throw new InvalidOperationException("MCP_WRITE_ALLOWED_TARGETS is required when MCP_WRITE_MODE is 'on'. Use a comma-separated list of host:port/database.");

        var maxRowsText = readVariable("MCP_WRITE_MAX_ROWS");
        var maxRows = 50;
        if (!string.IsNullOrWhiteSpace(maxRowsText) && (!int.TryParse(maxRowsText, out maxRows) || maxRows < 1))
            throw new InvalidOperationException("MCP_WRITE_MAX_ROWS must be a positive integer.");

        var (webhookUrl, webhookSecret) = confirmation == WriteConfirmationMode.Webhook
            ? ReadWebhook(readVariable)
            : (null, string.Empty);

        return new WriteOptions
        {
            ModeOn = true,
            Confirmation = confirmation,
            AllowedTargets = targets,
            MaxRows = maxRows,
            WebhookUrl = webhookUrl,
            WebhookSecret = webhookSecret
        };
    }

    private static WriteConfirmationMode ParseConfirmation(string? value) =>
        (value ?? "none").Trim().ToLowerInvariant() switch
        {
            "none" => WriteConfirmationMode.None,
            "elicitation" => WriteConfirmationMode.Elicitation,
            "webhook" => WriteConfirmationMode.Webhook,
            _ => throw new InvalidOperationException("MCP_WRITE_CONFIRMATION must be 'elicitation', 'webhook' or 'none'.")
        };

    private static (Uri? Url, string Secret) ReadWebhook(Func<string, string?> readVariable)
    {
        var urlText = readVariable("MCP_WRITE_CONFIRM_WEBHOOK_URL");
        var secret = readVariable("MCP_WRITE_CONFIRM_WEBHOOK_SECRET") ?? string.Empty;

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("MCP_WRITE_CONFIRM_WEBHOOK_URL must be an absolute http or https URL when MCP_WRITE_CONFIRMATION is 'webhook'.");

        if (url.Scheme == "http" && !IsLoopback(url))
            throw new InvalidOperationException("MCP_WRITE_CONFIRM_WEBHOOK_URL must use https unless it points to localhost.");

        if (secret.Length < MinimumWebhookSecretLength)
            throw new InvalidOperationException($"MCP_WRITE_CONFIRM_WEBHOOK_SECRET must have at least {MinimumWebhookSecretLength} characters. Generate one with: openssl rand -hex 32");

        return (url, secret);
    }

    private static bool IsLoopback(Uri url) =>
        url.IsLoopback || (IPAddress.TryParse(url.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
