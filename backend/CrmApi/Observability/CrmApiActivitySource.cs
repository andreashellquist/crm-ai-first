using System.Diagnostics;

namespace CrmApi.Observability;

// Shared ActivitySource for custom spans outside the auto-instrumented
// ASP.NET Core/HttpClient/Npgsql layers — background jobs and AI calls,
// which don't have their own inbound HTTP request to hang a span off of.
// Registered via .AddSource(Name) in Program.cs's OpenTelemetry setup.
public static class CrmApiActivitySource
{
    public const string Name = "CrmApi";
    public static readonly ActivitySource Instance = new(Name);
}
