using System.Text;
using CrmApi.Data;
using CrmApi.Observability;
using CrmApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Error tracking (devops-observability-expert). An *empty string* Dsn is a
// documented no-op for the Sentry SDK — but a null Dsn (i.e. the config key
// is absent entirely, not just unset — appsettings.json ships no
// Production-environment file, so unless Sentry:Dsn/SENTRY_DSN is injected
// by the platform, Configuration[...] returns null here) makes SDK init
// throw at startup instead. Coerce to "" so this stays dormant until a real
// DSN is configured, never a hard dependency for local dev or a missing-env-var
// footgun in prod.
builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"] ?? "";
    options.Environment = builder.Environment.EnvironmentName;
});

// Structured logs in Production (JSON, machine-parseable); human-readable
// console output everywhere else — see observability-and-slo skill.
// IncludeScopes surfaces the WorkspaceId/TraceId pushed by the request-scope
// middleware below and by JobWorker's per-job scope — without it the
// scope data is tracked but never actually printed.
if (builder.Environment.IsProduction())
{
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
}
else
{
    builder.Logging.AddSimpleConsole(o => o.IncludeScopes = true);
}

var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("crm-api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(CrmApiActivitySource.Name)
            .AddSource("Npgsql") // Npgsql's own built-in ActivitySource — no dedicated tracing extension package
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        // Console exporter in Development only — it's noisy to leave on
        // everywhere, and Test's WebApplicationFactory instances would spam
        // stdout on every request. Production traces only ship if an OTLP
        // collector is actually configured (wired but dormant otherwise,
        // same posture as Sentry above).
        if (builder.Environment.IsDevelopment())
        {
            tracing.AddConsoleExporter();
        }
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsqlInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<JobQueueService>();
builder.Services.AddScoped<DealScoringService>();
builder.Services.AddScoped<EmailDraftingService>();
builder.Services.AddScoped<SummarizationService>();
builder.Services.AddScoped<NextBestActionService>();
builder.Services.AddScoped<ContactImportService>();
builder.Services.AddSingleton<IAnthropicMessagesClient, AnthropicMessagesClient>();
builder.Services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>();
builder.Services.AddHostedService<JobWorker>();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // JwtSecurityTokenHandler's default inbound claim mapping silently
        // rewrites short claim type names (including "role") to long
        // legacy XML-namespace URIs on the way in — CurrentUser.Role reads
        // the short "role" name JwtService.GenerateToken actually wrote, so
        // without this every request would resolve to Role's "member"
        // fallback regardless of the token's real role. "workspaceId" isn't
        // one of the remapped well-known names, which is why that claim
        // always worked and masked this. See RequireRoleAttribute.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (args.Contains("seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Applies migrations first so this command is a self-sufficient
    // bootstrap for a fresh database (local dev, e2e/CI) — no separate
    // `dotnet ef database update` step needed. CrmApiFactory does the same
    // for the xUnit test database.
    await db.Database.MigrateAsync();
    await Seed.RunAsync(db);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Anonymous — used as the Playwright webServer readiness probe (e2e/playwright.config.ts)
// and generally useful as an uptime check; carries no data worth authenticating.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();

// Enriches every log line for the rest of the request with workspaceId (once
// known, post-auth) and the current trace ID — see observability-and-slo:
// "a structured log entry ... including workspaceId (when applicable), the
// trace ID." Placed after UseAuthentication so HttpContext.User is
// populated, before UseAuthorization/MapControllers so it wraps everything
// downstream, including 403s from RequireRoleAttribute.
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestScope");
    var scopeData = new Dictionary<string, object?>
    {
        ["TraceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString(),
    };
    if (context.User.Identity?.IsAuthenticated == true)
    {
        scopeData["WorkspaceId"] = context.RequestServices.GetRequiredService<CurrentUser>().WorkspaceId;
    }
    using (logger.BeginScope(scopeData))
    {
        await next(context);
    }
});

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in CrmApi.Tests.
public partial class Program { }
