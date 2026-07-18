// AI eval harness — Phase 1 per docs/PRODUCT_SCOPE.md and qa-test-engineer:
// "maintain a small fixed set of representative scenarios with expected
// qualities that a human or a rubric-based check can review whenever a
// prompt changes." Calls the REAL Claude API (real cost, real latency) —
// deliberately NOT part of `dotnet test`/CI. Run manually:
//
//   ANTHROPIC_API_KEY=sk-... dotnet run --project backend/CrmApi.Eval
//
// Exit code is 0 unless the harness itself failed (API error, DB error) —
// a scenario landing outside its expected score band is a WARN, not a
// build-breaking failure, because judging "is this rationale actually
// good" is exactly the part that still needs a human to eyeball it.

using CrmApi.Data;
using CrmApi.Eval;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY is not set — this harness calls the real Claude API and needs a real key.");
    return 1;
}

var connectionString = Environment.GetEnvironmentVariable("EVAL_DATABASE_URL")
    ?? "Host=localhost;Port=5432;Database=crm_dotnet_eval;Username=crm;Password=crm";

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = apiKey })
    .Build();

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));

var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
await using var db = new AppDbContext(dbOptions);
await db.Database.MigrateAsync();

var anthropic = new AnthropicMessagesClient(config);
var scoring = new DealScoringService(db, anthropic, loggerFactory.CreateLogger<DealScoringService>());

var scenario = await EvalFixtures.SeedAsync(db);
var results = new List<(EvalScenario Scenario, ScoreResult? Result, Exception? Error)>();

Console.WriteLine($"Running {scenario.Deals.Count} deal-scoring scenarios against the real Claude API...\n");

foreach (var (evalScenario, deal) in scenario.Deals)
{
    try
    {
        var result = await scoring.ScoreDeal(deal.Id, scenario.Workspace.Id);
        results.Add((evalScenario, result, null));
    }
    catch (Exception ex)
    {
        results.Add((evalScenario, null, ex));
    }
}

await EvalFixtures.CleanupAsync(db, scenario);

var anyHardFailure = false;
foreach (var (evalScenario, result, error) in results)
{
    Console.WriteLine($"=== {evalScenario.Name} ===");
    Console.WriteLine($"  {evalScenario.Description}");
    Console.WriteLine($"  Expected score band: {evalScenario.ExpectedScoreMin}-{evalScenario.ExpectedScoreMax}");

    if (error is not null)
    {
        anyHardFailure = true;
        Console.WriteLine($"  HARNESS ERROR: {error.Message}");
    }
    else if (result is not null)
    {
        var inBand = result.Score >= evalScenario.ExpectedScoreMin && result.Score <= evalScenario.ExpectedScoreMax;
        Console.WriteLine($"  Score: {result.Score} [{(inBand ? "PASS" : "WARN — outside expected band")}]");
        Console.WriteLine($"  Rationale: {result.Rationale}");
        Console.WriteLine($"  Signals: {string.Join("; ", result.Signals)}");
    }
    Console.WriteLine();
}

Console.WriteLine(anyHardFailure
    ? "One or more scenarios hit a harness error (not a scoring quality issue) — see above."
    : "All scenarios ran. Review each rationale above for quality — the score band is a sanity check, not a substitute for reading it.");

return anyHardFailure ? 1 : 0;
