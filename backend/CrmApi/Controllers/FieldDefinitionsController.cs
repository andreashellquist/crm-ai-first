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
[Route("api/field-definitions")]
[Authorize]
public class FieldDefinitionsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    private static readonly string[] ValidEntityTypes = ["contact", "company", "deal"];
    private static readonly string[] ValidFieldTypes = ["text", "number", "select", "date", "boolean"];

    [HttpGet]
    public async Task<ActionResult<List<FieldDefinitionDto>>> List([FromQuery] string? entityType)
    {
        var query = db.FieldDefinitions.Where(f => f.WorkspaceId == current.WorkspaceId);
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(f => f.EntityType == entityType);

        var defs = await query.OrderBy(f => f.Order).ToListAsync();
        return Ok(defs.Select(ToDto).ToList());
    }

    [HttpPost]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<FieldDefinitionDto>> Create(CreateFieldDefinitionRequest request)
    {
        if (!ValidEntityTypes.Contains(request.EntityType)) return BadRequest("Invalid entity type");
        if (!ValidFieldTypes.Contains(request.FieldType)) return BadRequest("Invalid field type");
        if (string.IsNullOrWhiteSpace(request.Key)) return BadRequest("Key is required");
        if (string.IsNullOrWhiteSpace(request.Label)) return BadRequest("Label is required");
        if (request.FieldType == "select" && (request.Options is null || request.Options.Count == 0))
            return BadRequest("Select fields need at least one option");

        var exists = await db.FieldDefinitions.AnyAsync(f =>
            f.WorkspaceId == current.WorkspaceId && f.EntityType == request.EntityType && f.Key == request.Key);
        if (exists) return Conflict($"Field \"{request.Key}\" already exists for {request.EntityType}");

        var def = new FieldDefinition
        {
            WorkspaceId = current.WorkspaceId,
            EntityType = request.EntityType,
            Key = request.Key,
            Label = request.Label,
            FieldType = request.FieldType,
            Options = request.Options is null ? null : JsonSerializer.Serialize(request.Options),
            Required = request.Required,
            Order = request.Order,
        };
        db.FieldDefinitions.Add(def);
        await db.SaveChangesAsync();
        return Ok(ToDto(def));
    }

    [HttpPut("{id}")]
    [RequireRole("owner", "admin")]
    public async Task<ActionResult<FieldDefinitionDto>> Update(string id, UpdateFieldDefinitionRequest request)
    {
        var def = await db.FieldDefinitions.FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == current.WorkspaceId);
        if (def is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Label)) return BadRequest("Label is required");
        if (def.FieldType == "select" && (request.Options is null || request.Options.Count == 0))
            return BadRequest("Select fields need at least one option");

        // EntityType/Key/FieldType are stable identity once values may exist
        // against them — only label/options/required/order are editable.
        def.Label = request.Label;
        def.Options = request.Options is null ? null : JsonSerializer.Serialize(request.Options);
        def.Required = request.Required;
        def.Order = request.Order;
        await db.SaveChangesAsync();
        return Ok(ToDto(def));
    }

    [HttpDelete("{id}")]
    [RequireRole("owner", "admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var def = await db.FieldDefinitions.FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == current.WorkspaceId);
        if (def is null) return NotFound();

        db.FieldDefinitions.Remove(def);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static FieldDefinitionDto ToDto(FieldDefinition def) => new(
        def.Id,
        def.EntityType,
        def.Key,
        def.Label,
        def.FieldType,
        def.Options is null ? null : JsonSerializer.Deserialize<List<string>>(def.Options),
        def.Required,
        def.Order
    );
}
