using System.Security.Cryptography;
using System.Text;
using Mcp.SqlServer.Configuration;
using Mcp.SqlServer.Services;
using Mcp.SqlServer.Tools;

var options = RuntimeOptions.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<DatabaseService>();

var mcpServer = builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

if (options.Write.Enabled)
{
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<PendingWriteStore>();
    builder.Services.AddSingleton<WriteService>();
    builder.Services.AddHttpClient<ConfirmationWebhook>(client => client.Timeout = TimeSpan.FromSeconds(10));
    mcpServer.WithTools<WriteTools>();
}

var app = builder.Build();

if (options.Write.Enabled)
    app.Logger.LogInformation("Write tools enabled: confirmation={Confirmation}, max rows={MaxRows}",
        options.Write.Confirmation, options.Write.MaxRows);
else if (options.Write.ModeOn)
    app.Logger.LogWarning("MCP_WRITE_MODE is on but MCP_WRITE_CONFIRMATION is none: write tools are disabled.");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var expectedToken = Encoding.UTF8.GetBytes(options.ApiToken);
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    const string prefix = "Bearer ";
    var header = context.Request.Headers.Authorization.ToString();
    var authorized = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()), expectedToken);

    if (!authorized)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    await next();
});

app.MapMcp("/mcp");
app.Run();
