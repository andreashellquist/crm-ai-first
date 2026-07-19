using System.Text.Json;
using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers.V1;

// Lets an integrator discover what a workspace's customFields keys mean —
// see the public-api-and-webhooks skill: "a separate endpoint to fetch that
// workspace's field definitions so integrators can interpret them."
[ApiController]
[Route("api/v1/field-definitions")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("ApiKey")]
public class PublicFieldDefinitionsController(AppDbContext db, Services.CurrentUser current) : ControllerBase
{
    [HttpGet]
    [RequireScope("field-definitions:read")]
    public async Task<ActionResult<List<FieldDefinitionDto>>> List([FromQuery] string? entityType)
    {
        var query = db.FieldDefinitions.Where(f => f.WorkspaceId == current.WorkspaceId);
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(f => f.EntityType == entityType);

        var defs = await query.OrderBy(f => f.Order).ToListAsync();
        return Ok(defs.Select(ToDto).ToList());
    }

    private static FieldDefinitionDto ToDto(Models.FieldDefinition def) => new(
        def.Id, def.EntityType, def.Key, def.Label, def.FieldType,
        def.Options is null ? null : JsonSerializer.Deserialize<List<string>>(def.Options),
        def.Required, def.Order
    );
}
