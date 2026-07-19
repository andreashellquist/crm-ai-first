using System.Text.Json;
using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers.V1;

// Public v1 API (public-api-and-webhooks skill) — authenticated by API key
// (X-Api-Key header), never the session JWT; every query still re-scopes by
// CurrentUser.WorkspaceId exactly like the rest of the app, since API-key
// auth resolves to the same claim session auth does.
[ApiController]
[Route("api/v1/contacts")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("ApiKey")]
public class PublicContactsController(AppDbContext db, Services.CurrentUser current) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    [HttpGet]
    [RequireScope("contacts:read")]
    public async Task<ActionResult<List<PublicContactDto>>> List([FromQuery] int? limit)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var contacts = await db.Contacts
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(contacts.Select(ToDto).ToList());
    }

    [HttpGet("{id}")]
    [RequireScope("contacts:read")]
    public async Task<ActionResult<PublicContactDto>> Get(string id)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null);
        if (contact is null) return NotFound();
        return Ok(ToDto(contact));
    }

    private static PublicContactDto ToDto(Models.Contact c) => new(
        c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.CompanyId, c.LifecycleStage,
        c.CreatedAt, c.UpdatedAt,
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.CustomFields) ?? []
    );
}
