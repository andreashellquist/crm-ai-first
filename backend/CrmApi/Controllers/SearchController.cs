using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public class SearchController(AppDbContext db, CurrentUser current) : ControllerBase
{
    private const int PerCategoryLimit = 5;

    // ILIKE substring match, not Postgres full-text search — a deliberate v1
    // tradeoff (see database-schema-expert: "don't reach for [FTS] until
    // ILIKE demonstrably can't keep up"/"don't index speculatively"). Fine
    // at this app's SMB target scale (docs/PRODUCT_SCOPE.md: 5-500 seats);
    // upgrade to a generated tsvector column + GIN index if a workspace's
    // contact/company volume ever makes this measurably slow.
    [HttpGet]
    public async Task<ActionResult<SearchResultsDto>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new SearchResultsDto([], [], []));

        var pattern = $"%{q.Trim()}%";

        // Project raw fields first, then build the label/sublabel strings in
        // C# after materializing — string.Join over a filtered array doesn't
        // translate to SQL reliably, so keep that part client-side (same
        // split as ContactsController.ToDto).
        var contactRows = await db.Contacts
            .Include(c => c.Company)
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
            .Where(c => EF.Functions.ILike(c.FirstName ?? "", pattern)
                || EF.Functions.ILike(c.LastName ?? "", pattern)
                || EF.Functions.ILike(c.Email ?? "", pattern))
            .OrderByDescending(c => c.CreatedAt)
            .Take(PerCategoryLimit)
            .ToListAsync();
        var contacts = contactRows.Select(c => new SearchResultDto(
            c.Id,
            string.Join(" ", new[] { c.FirstName, c.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))),
            c.Email ?? c.Company?.Name)).ToList();

        var companies = await db.Companies
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
            .Where(c => EF.Functions.ILike(c.Name, pattern) || EF.Functions.ILike(c.Domain ?? "", pattern))
            .OrderByDescending(c => c.CreatedAt)
            .Take(PerCategoryLimit)
            .Select(c => new SearchResultDto(c.Id, c.Name, c.Domain))
            .ToListAsync();

        // Deal has no free-text title of its own — the UI displays a deal by
        // its Company's name (see PipelineController.ToDealDetailDto), so
        // that's what search matches against here too.
        var deals = await db.Deals
            .Include(d => d.Company)
            .Include(d => d.Stage)
            .Where(d => d.WorkspaceId == current.WorkspaceId && d.DeletedAt == null && d.Company != null)
            .Where(d => EF.Functions.ILike(d.Company!.Name, pattern))
            .OrderByDescending(d => d.UpdatedAt)
            .Take(PerCategoryLimit)
            .Select(d => new SearchResultDto(d.Id, d.Company!.Name, d.Stage!.Name))
            .ToListAsync();

        return Ok(new SearchResultsDto(contacts, companies, deals));
    }
}
