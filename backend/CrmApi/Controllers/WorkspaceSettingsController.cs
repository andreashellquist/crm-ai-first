using System.Text.Json;
using System.Text.RegularExpressions;
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
    // Sanity check, not a full ISO 4217 lookup table — catches obviously
    // wrong input without maintaining an exhaustive currency-code list here.
    private static readonly Regex CurrencyCodePattern = new("^[A-Z]{3}$");

    [HttpGet]
    public async Task<ActionResult<WorkspaceSettingsDto>> Get()
    {
        var workspace = await db.Workspaces.FirstAsync(w => w.Id == current.WorkspaceId);
        var settings = await db.WorkspaceSettings.FirstOrDefaultAsync(s => s.WorkspaceId == current.WorkspaceId);
        return Ok(ToDto(settings, workspace));
    }

    // Terminology overrides, enabled modules, and default currency change
    // how the whole workspace presents itself (and, for terminology, what
    // AI prompts say) — restricted to owner/admin per auth-security-expert's
    // "check role at the point of mutation."
    [HttpPut]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<WorkspaceSettingsDto>> Update(UpdateWorkspaceSettingsRequest request)
    {
        if (request.DefaultCurrency is not null && !CurrencyCodePattern.IsMatch(request.DefaultCurrency))
            return BadRequest("Default currency must be a 3-letter ISO 4217 code, e.g. \"USD\"");

        var workspace = await db.Workspaces.FirstAsync(w => w.Id == current.WorkspaceId);
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
        if (request.DefaultCurrency is not null)
            workspace.DefaultCurrency = request.DefaultCurrency;

        await db.SaveChangesAsync();
        return Ok(ToDto(settings, workspace));
    }

    private static WorkspaceSettingsDto ToDto(WorkspaceSettings? settings, Workspace workspace)
    {
        var terminology = settings is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.Terminology) ?? [];
        return new WorkspaceSettingsDto(terminology, settings?.EnabledModules ?? [], workspace.DefaultCurrency);
    }
}
