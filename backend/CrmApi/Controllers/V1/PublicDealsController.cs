using System.Text.Json;
using CrmApi.Authorization;
using CrmApi.Data;
using CrmApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers.V1;

[ApiController]
[Route("api/v1/deals")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("ApiKey")]
public class PublicDealsController(AppDbContext db, Services.CurrentUser current) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    [HttpGet]
    [RequireScope("deals:read")]
    public async Task<ActionResult<List<PublicDealDto>>> List([FromQuery] int? limit)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var deals = await db.Deals
            .Include(d => d.Contacts)
            .Where(d => d.WorkspaceId == current.WorkspaceId && d.DeletedAt == null)
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(deals.Select(ToDto).ToList());
    }

    [HttpGet("{id}")]
    [RequireScope("deals:read")]
    public async Task<ActionResult<PublicDealDto>> Get(string id)
    {
        var deal = await db.Deals
            .Include(d => d.Contacts)
            .FirstOrDefaultAsync(d => d.Id == id && d.WorkspaceId == current.WorkspaceId && d.DeletedAt == null);
        if (deal is null) return NotFound();
        return Ok(ToDto(deal));
    }

    private static PublicDealDto ToDto(Models.Deal d) => new(
        d.Id, d.CompanyId, d.PipelineId, d.StageId, d.AmountCents, d.Currency, d.ForecastCategory,
        d.Contacts.Select(c => c.Id).ToList(), d.ClosedAt, d.CreatedAt, d.UpdatedAt,
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(d.CustomFields) ?? []
    );
}
