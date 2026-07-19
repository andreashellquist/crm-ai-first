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
[Route("api/v1/companies")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("ApiKey")]
public class PublicCompaniesController(AppDbContext db, Services.CurrentUser current) : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    [HttpGet]
    [RequireScope("companies:read")]
    public async Task<ActionResult<List<PublicCompanyDto>>> List([FromQuery] int? limit)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var companies = await db.Companies
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(companies.Select(ToDto).ToList());
    }

    [HttpGet("{id}")]
    [RequireScope("companies:read")]
    public async Task<ActionResult<PublicCompanyDto>> Get(string id)
    {
        var company = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null);
        if (company is null) return NotFound();
        return Ok(ToDto(company));
    }

    private static PublicCompanyDto ToDto(Models.Company c) => new(
        c.Id, c.Name, c.Domain, c.CreatedAt, c.UpdatedAt,
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.CustomFields) ?? []
    );
}
