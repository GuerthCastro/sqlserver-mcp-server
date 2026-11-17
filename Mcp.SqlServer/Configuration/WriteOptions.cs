using System.Net;

namespace Mcp.SqlServer.Configuration;

public enum WriteConfirmationMode
{
    None,
    Elicitation,
    Webhook
}

public sealed class WriteOptions
{
    public const int MinimumWebhookSecretLength = 32;

    public static readonly WriteOptions Disabled = new();

    public bool ModeOn { get; init; }
    public WriteConfirmationMode Confirmation { get; init; } = WriteConfirmationMode.None;
    public int MaxRows { get; init; } = 50;
    public Uri? WebhookUrl { get; init; }
    public string WebhookSecret { get; init; } = string.Empty;

    public bool Enabled => ModeOn && Confirmation != WriteConfirmationMode.None;

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
