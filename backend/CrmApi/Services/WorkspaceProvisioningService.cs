using System.Text.Json;
using CrmApi.Data;
using CrmApi.Models;

namespace CrmApi.Services;

// Applies a VerticalTemplate to a freshly created, otherwise-bare workspace —
// see the workspace-customization skill §3. Called once, at account
// creation, by both AuthController.Register (explicit template choice) and
// AuthController.GoogleExchange's first-time-user path (defaults to
// VerticalTemplates.DefaultId, since the OAuth flow has no template-picker
// step). After this runs, the result is ordinary editable workspace data —
// Pipeline/Stage rows, WorkspaceSettings, FieldDefinition rows — no
// different from a workspace a user configured by hand.
public class WorkspaceProvisioningService(AppDbContext db)
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task ProvisionAsync(string workspaceId, string templateId)
    {
        var template = VerticalTemplates.Find(templateId) ?? VerticalTemplates.Find(VerticalTemplates.DefaultId)!;

        var pipeline = new Pipeline { WorkspaceId = workspaceId, Name = template.Pipeline.Name, IsDefault = true };
        db.Pipelines.Add(pipeline);

        for (var i = 0; i < template.Pipeline.Stages.Count; i++)
        {
            var stage = template.Pipeline.Stages[i];
            db.Stages.Add(new Stage
            {
                PipelineId = pipeline.Id,
                Name = stage.Name,
                Order = i,
                Probability = stage.Probability,
                IsWon = stage.IsWon,
                IsLost = stage.IsLost,
            });
        }

        db.WorkspaceSettings.Add(new WorkspaceSettings
        {
            WorkspaceId = workspaceId,
            Terminology = JsonSerializer.Serialize(template.Terminology, CamelCase),
            EnabledModules = template.SuggestedModules ?? [],
        });

        foreach (var field in template.Fields)
        {
            db.FieldDefinitions.Add(new FieldDefinition
            {
                WorkspaceId = workspaceId,
                EntityType = field.EntityType,
                Key = field.Key,
                Label = field.Label,
                FieldType = field.FieldType,
                Options = field.Options is null ? null : JsonSerializer.Serialize(field.Options),
            });
        }

        await db.SaveChangesAsync();
    }
}
