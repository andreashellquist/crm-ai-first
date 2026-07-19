using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

// The real-estate optional module (workspace-customization skill §4) — a
// Listing extends a Deal 1:1 and is only reachable when "listings" is in
// WorkspaceSettings.EnabledModules. Every other controller/flow in this app
// works correctly whether or not this module is enabled; nothing outside
// this controller ever references Listing.
[ApiController]
[Route("api/deals/{dealId}/listing")]
[Authorize]
public class ListingsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ListingDto>> Get(string dealId)
    {
        if (!await ModuleEnabledAsync()) return Forbid();

        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        if (deal is null) return NotFound();

        var listing = await db.Listings.FirstOrDefaultAsync(l => l.DealId == dealId && l.WorkspaceId == current.WorkspaceId);
        return Ok(ToDto(dealId, listing));
    }

    [HttpPut]
    public async Task<ActionResult<ListingDto>> Upsert(string dealId, UpsertListingRequest request)
    {
        if (!await ModuleEnabledAsync()) return Forbid();

        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId && d.WorkspaceId == current.WorkspaceId);
        if (deal is null) return NotFound();

        if (request.CommissionPercent is < 0 or > 100)
            return BadRequest("Commission percent must be between 0 and 100");

        var listing = await db.Listings.FirstOrDefaultAsync(l => l.DealId == dealId && l.WorkspaceId == current.WorkspaceId);
        if (listing is null)
        {
            listing = new Listing { WorkspaceId = current.WorkspaceId, DealId = dealId };
            db.Listings.Add(listing);
        }

        listing.ListingAgentName = request.ListingAgentName;
        listing.ListingUrl = request.ListingUrl;
        listing.OpenHouseAt = request.OpenHouseAt;
        listing.CommissionPercent = request.CommissionPercent;
        listing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(ToDto(dealId, listing));
    }

    private async Task<bool> ModuleEnabledAsync()
    {
        var enabledModules = await db.WorkspaceSettings
            .Where(s => s.WorkspaceId == current.WorkspaceId)
            .Select(s => s.EnabledModules)
            .FirstOrDefaultAsync();
        return enabledModules?.Contains("listings") ?? false;
    }

    private static ListingDto ToDto(string dealId, Listing? listing) => new(
        dealId,
        listing?.ListingAgentName,
        listing?.ListingUrl,
        listing?.OpenHouseAt,
        listing?.CommissionPercent,
        listing?.UpdatedAt ?? default
    );
}
