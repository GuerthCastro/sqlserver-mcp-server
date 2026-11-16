using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Mcp.SqlServer.Services;

namespace Mcp.SqlServer.Tests.Services;

public class WebhookSignatureTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef";
    private const long Timestamp = 1_790_000_000;
    private const string Body = """{"writeId":"abc","code":"123456"}""";

    [Fact]
    public void Compute_ShouldMatchHmacOfTimestampAndBody_WhenReceiverVerifiesIt()
    {
        // Arrange
        var expected = ReceiverSignature(Secret, Timestamp, Body);

        // Act
        var signature = WebhookSignature.Compute(Secret, Timestamp, Body);

        // Assert
        signature.Should().Be(expected);
    }

    [Theory]
    [InlineData(Secret, Timestamp + 1, Body)]
    [InlineData(Secret, Timestamp, """{"writeId":"abc","code":"654321"}""")]
    [InlineData("ffffffffffffffffffffffffffffffff", Timestamp, Body)]
    public void Compute_ShouldNotMatch_WhenTimestampBodyOrSecretIsTampered(string secret, long timestamp, string body)
    {
        // Arrange
        var receivedSignature = WebhookSignature.Compute(Secret, Timestamp, Body);

        // Act
        var receiverSignature = ReceiverSignature(secret, timestamp, body);

        // Assert
        receiverSignature.Should().NotBe(receivedSignature);
    }

    private static string ReceiverSignature(string secret, long timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + string.Concat(hash.Select(value => value.ToString("x2")));
    }
}
