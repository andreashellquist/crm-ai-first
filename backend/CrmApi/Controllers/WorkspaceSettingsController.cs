using System.Text.Json;
using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/workspace/settings")]
[Authorize]
public class WorkspaceSettingsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WorkspaceSettingsDto>> Get()
    {
        var settings = await db.WorkspaceSettings.FirstOrDefaultAsync(s => s.WorkspaceId == current.WorkspaceId);
        return Ok(ToDto(settings));
    }

    // Terminology overrides and enabled modules change how the whole
    // workspace presents itself (and, for terminology, what AI prompts say)
    // — restricted to owner/admin per auth-security-expert's "check role at
    // the point of mutation."
    [HttpPut]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<WorkspaceSettingsDto>> Update(UpdateWorkspaceSettingsRequest request)
    {
        var settings = await db.WorkspaceSettings.FirstOrDefaultAsync(s => s.WorkspaceId == current.WorkspaceId);
        if (settings is null)
        {
            settings = new WorkspaceSettings { WorkspaceId = current.WorkspaceId };
            db.WorkspaceSettings.Add(settings);
        }

        if (request.Terminology is not null)
            settings.Terminology = JsonSerializer.Serialize(request.Terminology);
        if (request.EnabledModules is not null)
            settings.EnabledModules = request.EnabledModules;

        await db.SaveChangesAsync();
        return Ok(ToDto(settings));
    }

    private static WorkspaceSettingsDto ToDto(WorkspaceSettings? settings)
    {
        var terminology = settings is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.Terminology) ?? [];
        return new WorkspaceSettingsDto(terminology, settings?.EnabledModules ?? []);
    }
}
