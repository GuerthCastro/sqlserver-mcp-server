using System.Security.Cryptography;
using System.Text;
using Mcp.SqlServer.Configuration;
using Mcp.SqlServer.Services;

var options = RuntimeOptions.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

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
