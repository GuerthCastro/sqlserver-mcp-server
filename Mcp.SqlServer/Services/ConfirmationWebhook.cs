using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mcp.SqlServer.Configuration;

namespace Mcp.SqlServer.Services;

public static class WebhookSignature
{
    public const string SignatureHeader = "X-Signature";
    public const string TimestampHeader = "X-Timestamp";

    public static string Compute(string secret, long timestamp, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }
}

public sealed class ConfirmationWebhook(HttpClient httpClient, RuntimeOptions options, TimeProvider timeProvider, ILogger<ConfirmationWebhook> logger)
{
    public async Task<bool> SendCodeAsync(PendingWrite pending, string code)
    {
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var body = JsonSerializer.Serialize(new
        {
            @event = "write.confirmation_requested",
            server = "sqlserver-mcp-server",
            writeId = pending.WriteId,
            code,
            timestamp,
            expiresAt = pending.ExpiresAt.ToString("O"),
            target = new { host = pending.Host, port = pending.Port, database = pending.Database },
            operation = pending.Statement.Operation.ToString().ToUpperInvariant(),
            table = pending.TableName,
            rows = pending.ExpectedRows,
            sql = pending.Statement.Sql
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Write.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(WebhookSignature.TimestampHeader, timestamp.ToString());
        request.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Compute(options.Write.WebhookSecret, timestamp, body));

        try
        {
            using var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("Confirmation webhook rejected writeId {WriteId} with status {StatusCode}", pending.WriteId, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Confirmation webhook failed for writeId {WriteId}: {ExceptionType}", pending.WriteId, ex.GetType().Name);
            return false;
        }
    }
}
