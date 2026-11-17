using AwesomeAssertions;
using Mcp.SqlServer.Configuration;

namespace Mcp.SqlServer.Tests.Configuration;

public class WriteOptionsTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void From_ShouldDisableWrites_WhenModeIsNotSet()
    {
        // Arrange
        var variables = new Dictionary<string, string>();

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.Enabled.Should().BeFalse();
        options.ModeOn.Should().BeFalse();
    }

    [Fact]
    public void From_ShouldDisableWrites_WhenModeIsOnAndConfirmationIsNone()
    {
        // Arrange
        var variables = new Dictionary<string, string> { ["MCP_WRITE_MODE"] = "on", ["MCP_WRITE_CONFIRMATION"] = "none" };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.ModeOn.Should().BeTrue();
        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void From_ShouldParseDefaults_WhenWebhookIsConfigured()
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["MCP_WRITE_MODE"] = "on",
            ["MCP_WRITE_CONFIRMATION"] = "webhook",
            ["MCP_WRITE_CONFIRM_WEBHOOK_URL"] = "https://hooks.example.test/confirm",
            ["MCP_WRITE_CONFIRM_WEBHOOK_SECRET"] = Secret
        };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.Enabled.Should().BeTrue();
        options.MaxRows.Should().Be(50);
    }

    [Fact]
    public void From_ShouldEnableWrites_WhenElicitationIsConfigured()
    {
        // Arrange
        var variables = new Dictionary<string, string> { ["MCP_WRITE_MODE"] = "on", ["MCP_WRITE_CONFIRMATION"] = "elicitation" };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.Enabled.Should().BeTrue();
        options.Confirmation.Should().Be(WriteConfirmationMode.Elicitation);
        options.MaxRows.Should().Be(50);
    }

    [Theory]
    [InlineData("MCP_WRITE_MODE", "yes")]
    [InlineData("MCP_WRITE_CONFIRMATION", "email")]
    [InlineData("MCP_WRITE_MAX_ROWS", "0")]
    [InlineData("MCP_WRITE_CONFIRM_WEBHOOK_URL", "http://hooks.example.test/confirm")]
    [InlineData("MCP_WRITE_CONFIRM_WEBHOOK_URL", "not a url")]
    [InlineData("MCP_WRITE_CONFIRM_WEBHOOK_SECRET", "too-short")]
    public void From_ShouldFailFast_WhenWriteConfigurationIsInvalid(string variable, string value)
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["MCP_WRITE_MODE"] = "on",
            ["MCP_WRITE_CONFIRMATION"] = "webhook",
            ["MCP_WRITE_CONFIRM_WEBHOOK_URL"] = "https://hooks.example.test/confirm",
            ["MCP_WRITE_CONFIRM_WEBHOOK_SECRET"] = Secret,
            [variable] = value
        };

        // Act
        var act = () => WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("http://localhost:8080/confirm")]
    [InlineData("http://127.0.0.1:8080/confirm")]
    public void From_ShouldAcceptHttpWebhook_WhenUrlIsLoopback(string url)
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["MCP_WRITE_MODE"] = "on",
            ["MCP_WRITE_CONFIRMATION"] = "webhook",
            ["MCP_WRITE_CONFIRM_WEBHOOK_URL"] = url,
            ["MCP_WRITE_CONFIRM_WEBHOOK_SECRET"] = Secret
        };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.WebhookUrl.Should().Be(new Uri(url));
    }
}
