using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Data;

public static class Seed
{
    private const string DevEmail = "demo@example.com";
    private const string DevPassword = "password123";

    public static async Task RunAsync(AppDbContext db)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == DevEmail);
        if (user is null)
        {
            user = new User
            {
                Email = DevEmail,
                Name = "Demo Rep",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DevPassword),
            };
            db.Users.Add(user);
        }

        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Name == "Acme Sales");
        if (workspace is null)
        {
            workspace = new Workspace { Name = "Acme Sales" };
            db.Workspaces.Add(workspace);
        }
        await db.SaveChangesAsync();

        if (!await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspace.Id && m.UserId == user.Id))
        {
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = "owner" });
        }

        var pipeline = await db.Pipelines.FirstOrDefaultAsync(p => p.WorkspaceId == workspace.Id && p.IsDefault);
        if (pipeline is null)
        {
            pipeline = new Pipeline { WorkspaceId = workspace.Id, Name = "New Business", IsDefault = true };
            db.Pipelines.Add(pipeline);
            await db.SaveChangesAsync();

            var stageDefs = new (string Name, int Order, int Probability, bool IsWon, bool IsLost)[]
            {
                ("Prospecting", 0, 10, false, false),
                ("Qualified", 1, 30, false, false),
                ("Proposal", 2, 60, false, false),
                ("Negotiation", 3, 80, false, false),
                ("Closed Won", 4, 100, true, false),
                ("Closed Lost", 5, 0, false, true),
            };
            foreach (var s in stageDefs)
            {
                db.Stages.Add(new Stage
                {
                    PipelineId = pipeline.Id,
                    Name = s.Name,
                    Order = s.Order,
                    Probability = s.Probability,
                    IsWon = s.IsWon,
                    IsLost = s.IsLost,
                });
            }
            await db.SaveChangesAsync();
        }

        var company = await db.Companies.FirstOrDefaultAsync(c => c.WorkspaceId == workspace.Id && c.Name == "Globex Corporation");
        if (company is null)
        {
            company = new Company { WorkspaceId = workspace.Id, Name = "Globex Corporation", Domain = "globex.example" };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
        }

        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.WorkspaceId == workspace.Id && c.Email == "jane.doe@globex.example");
        if (contact is null)
        {
            contact = new Contact
            {
                WorkspaceId = workspace.Id,
                CompanyId = company.Id,
                FirstName = "Jane",
                LastName = "Doe",
                Email = "jane.doe@globex.example",
                LifecycleStage = "opportunity",
            };
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
        }

        var qualifiedStage = await db.Stages.FirstAsync(s => s.PipelineId == pipeline.Id && s.Name == "Qualified");
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.WorkspaceId == workspace.Id && d.CompanyId == company.Id);
        if (deal is null)
        {
            deal = new Deal
            {
                WorkspaceId = workspace.Id,
                PipelineId = pipeline.Id,
                StageId = qualifiedStage.Id,
                CompanyId = company.Id,
                AmountCents = 4_500_000,
                Currency = "USD",
                Contacts = [contact],
            };
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
        }

        if (!await db.Activities.AnyAsync(a => a.DealId == deal.Id))
        {
            db.Activities.AddRange(
                new Activity
                {
                    WorkspaceId = workspace.Id,
                    DealId = deal.Id,
                    CompanyId = company.Id,
                    Type = "call",
                    Body = "Intro call with Jane. Interested in the Enterprise tier, wants a security review before signing.",
                },
                new Activity
                {
                    WorkspaceId = workspace.Id,
                    DealId = deal.Id,
                    CompanyId = company.Id,
                    Type = "email",
                    Body = "Sent pricing breakdown and a comparison against their current vendor.",
                }
            );
            await db.SaveChangesAsync();
        }

        Console.WriteLine($"Seeded. Sign in with {DevEmail} / {DevPassword}");
    }
}
