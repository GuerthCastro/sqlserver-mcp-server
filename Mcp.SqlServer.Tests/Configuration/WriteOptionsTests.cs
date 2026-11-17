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
    public void From_ShouldParseTargetsAndDefaults_WhenWebhookIsConfigured()
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["MCP_WRITE_MODE"] = "on",
            ["MCP_WRITE_CONFIRMATION"] = "webhook",
            ["MCP_WRITE_ALLOWED_TARGETS"] = "db.example.test:1433/app, localhost:1434/sandbox",
            ["MCP_WRITE_CONFIRM_WEBHOOK_URL"] = "https://hooks.example.test/confirm",
            ["MCP_WRITE_CONFIRM_WEBHOOK_SECRET"] = Secret
        };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.Enabled.Should().BeTrue();
        options.MaxRows.Should().Be(50);
        options.AllowedTargets.Should().Equal(new WriteTarget("db.example.test", 1433, "app"), new WriteTarget("localhost", 1434, "sandbox"));
        options.IsAllowed("DB.EXAMPLE.TEST", 1433, "app").Should().BeTrue();
        options.IsAllowed("db.example.test", 1433, "App").Should().BeTrue();
        options.IsAllowed("db.example.test", 1434, "app").Should().BeFalse();
        options.IsAllowed("other.example.test", 1433, "app").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void From_ShouldAllowAnyTarget_WhenAllowedTargetsIsNotSet(string? targets)
    {
        // Arrange
        var variables = new Dictionary<string, string?>
        {
            ["MCP_WRITE_MODE"] = "on",
            ["MCP_WRITE_CONFIRMATION"] = "elicitation",
            ["MCP_WRITE_ALLOWED_TARGETS"] = targets
        };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.Enabled.Should().BeTrue();
        options.AllowedTargets.Should().BeEmpty();
        options.IsAllowed("db.example.test", 1433, "app").Should().BeTrue();
        options.IsAllowed("other.example.test", 1, "sandbox").Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_ShouldRejectUnlistedTarget_WhenAllowedTargetsIsSet()
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["MCP_WRITE_MODE"] = "on",
            ["MCP_WRITE_CONFIRMATION"] = "elicitation",
            ["MCP_WRITE_ALLOWED_TARGETS"] = "db.example.test:1433/app"
        };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.IsAllowed("db.example.test", 1433, "app").Should().BeTrue();
        options.IsAllowed("db.example.test", 1433, "sandbox").Should().BeFalse();
        options.IsAllowed("other.example.test", 1433, "app").Should().BeFalse();
    }

    [Theory]
    [InlineData("MCP_WRITE_MODE", "yes")]
    [InlineData("MCP_WRITE_CONFIRMATION", "email")]
    [InlineData("MCP_WRITE_ALLOWED_TARGETS", "db.example.test/app")]
    [InlineData("MCP_WRITE_ALLOWED_TARGETS", "db.example.test:port/app")]
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
            ["MCP_WRITE_ALLOWED_TARGETS"] = "db.example.test:1433/app",
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
            ["MCP_WRITE_ALLOWED_TARGETS"] = "db.example.test:1433/app",
            ["MCP_WRITE_CONFIRM_WEBHOOK_URL"] = url,
            ["MCP_WRITE_CONFIRM_WEBHOOK_SECRET"] = Secret
        };

        // Act
        var options = WriteOptions.From(variables.GetValueOrDefault);

        // Assert
        options.WebhookUrl.Should().Be(new Uri(url));
    }
}
