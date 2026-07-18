using System.Text.Json;
using CrmApi.Data;
using CrmApi.Models;

namespace CrmApi.Services;

public class JobQueueService(AppDbContext db)
{
    public async Task<string> Enqueue(string type, object payload, string? workspaceId, int maxAttempts = 3)
    {
        var job = new Job
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload),
            WorkspaceId = workspaceId,
            MaxAttempts = maxAttempts,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }
}
