using CrmApi.Data;
using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Eval;

public record EvalScenario(string Name, string Description, int ExpectedScoreMin, int ExpectedScoreMax);

public record EvalScenarioSet(Workspace Workspace, List<(EvalScenario Scenario, Deal Deal)> Deals);

// Fixed, representative deal-scoring scenarios — see ai-features-architect's
// Evaluation section and qa-test-engineer's eval-set guidance. Score bands
// are deliberately wide: this is a sanity check that the model is roughly
// tracking the signals, not a precision target — the rationale text is what
// actually needs a human read.
public static class EvalFixtures
{
    public static readonly EvalScenario HotLead = new(
        "Hot lead, closing soon",
        "Late-stage deal, high win-probability stage, forecast=commit, recent positive engagement.",
        ExpectedScoreMin: 65, ExpectedScoreMax: 100);

    public static readonly EvalScenario StalledDeal = new(
        "Stalled deal, gone quiet",
        "Early-stage, no activity in 90+ days, forecast=pipeline.",
        ExpectedScoreMin: 0, ExpectedScoreMax: 40);

    public static readonly EvalScenario PromisingEarlyStage = new(
        "Early-stage but promising",
        "Mid-stage, multiple engaged contacts, recent positive activity, forecast=best_case.",
        ExpectedScoreMin: 40, ExpectedScoreMax: 80);

    public static readonly EvalScenario ObjectionRaised = new(
        "Recent objection/pushback",
        "Mid-late stage but the most recent activity records a budget objection, forecast=pipeline.",
        ExpectedScoreMin: 10, ExpectedScoreMax: 45);

    public static async Task<EvalScenarioSet> SeedAsync(AppDbContext db)
    {
        var workspace = new Workspace { Name = $"Eval Run {DateTime.UtcNow:O}" };
        var company = new Company { WorkspaceId = workspace.Id, Name = "Eval Test Co" };
        var pipeline = new Pipeline { WorkspaceId = workspace.Id, Name = "Eval Pipeline", IsDefault = true };
        var discovery = new Stage { PipelineId = pipeline.Id, Name = "Discovery", Order = 0, Probability = 10 };
        var proposal = new Stage { PipelineId = pipeline.Id, Name = "Proposal", Order = 1, Probability = 40 };
        var negotiation = new Stage { PipelineId = pipeline.Id, Name = "Negotiation", Order = 2, Probability = 70 };
        var committed = new Stage { PipelineId = pipeline.Id, Name = "Committed", Order = 3, Probability = 90 };

        db.Workspaces.Add(workspace);
        db.Companies.Add(company);
        db.Pipelines.Add(pipeline);
        db.Stages.AddRange(discovery, proposal, negotiation, committed);
        await db.SaveChangesAsync();

        var hotLeadDeal = new Deal
        {
            WorkspaceId = workspace.Id, PipelineId = pipeline.Id, StageId = committed.Id, CompanyId = company.Id,
            AmountCents = 8_500_000, Currency = "USD", ForecastCategory = "commit",
            UpdatedAt = DateTime.UtcNow.AddDays(-2),
        };
        var stalledDeal = new Deal
        {
            WorkspaceId = workspace.Id, PipelineId = pipeline.Id, StageId = discovery.Id, CompanyId = company.Id,
            AmountCents = 1_200_000, Currency = "USD", ForecastCategory = "pipeline",
            UpdatedAt = DateTime.UtcNow.AddDays(-95),
        };
        var promisingDeal = new Deal
        {
            WorkspaceId = workspace.Id, PipelineId = pipeline.Id, StageId = proposal.Id, CompanyId = company.Id,
            AmountCents = 4_000_000, Currency = "USD", ForecastCategory = "best_case",
            UpdatedAt = DateTime.UtcNow.AddDays(-3),
        };
        var objectionDeal = new Deal
        {
            WorkspaceId = workspace.Id, PipelineId = pipeline.Id, StageId = negotiation.Id, CompanyId = company.Id,
            AmountCents = 6_000_000, Currency = "USD", ForecastCategory = "pipeline",
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
        };
        db.Deals.AddRange(hotLeadDeal, stalledDeal, promisingDeal, objectionDeal);
        await db.SaveChangesAsync();

        db.Activities.AddRange(
            new Activity { WorkspaceId = workspace.Id, DealId = hotLeadDeal.Id, Type = "email", Body = "Legal confirmed redlines are resolved, ready to countersign this week.", CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new Activity { WorkspaceId = workspace.Id, DealId = hotLeadDeal.Id, Type = "call", Body = "Champion confirmed budget is approved and signed off internally.", CreatedAt = DateTime.UtcNow.AddDays(-6) },

            new Activity { WorkspaceId = workspace.Id, DealId = stalledDeal.Id, Type = "email", Body = "Sent a follow-up after the initial discovery call, no response yet.", CreatedAt = DateTime.UtcNow.AddDays(-95) },

            new Activity { WorkspaceId = workspace.Id, DealId = promisingDeal.Id, Type = "meeting", Body = "Demo went well, three stakeholders attended and asked detailed implementation questions.", CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new Activity { WorkspaceId = workspace.Id, DealId = promisingDeal.Id, Type = "email", Body = "Prospect asked for a proposal tailored to their Q3 rollout timeline.", CreatedAt = DateTime.UtcNow.AddDays(-5) },

            new Activity { WorkspaceId = workspace.Id, DealId = objectionDeal.Id, Type = "call", Body = "Prospect said the proposed price is significantly over their approved budget and they may need to postpone to next fiscal year.", CreatedAt = DateTime.UtcNow.AddDays(-1) }
        );
        await db.SaveChangesAsync();

        return new EvalScenarioSet(workspace, [
            (HotLead, hotLeadDeal),
            (StalledDeal, stalledDeal),
            (PromisingEarlyStage, promisingDeal),
            (ObjectionRaised, objectionDeal),
        ]);
    }

    public static async Task CleanupAsync(AppDbContext db, EvalScenarioSet scenario)
    {
        // Cascade delete via Workspace removes the pipeline/stages/deals/
        // activities/company created for this run — see AppDbContext's
        // OnDelete(DeleteBehavior.Cascade) config on WorkspaceId FKs.
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == scenario.Workspace.Id);
        if (workspace is not null)
        {
            db.Workspaces.Remove(workspace);
            await db.SaveChangesAsync();
        }
    }
}
