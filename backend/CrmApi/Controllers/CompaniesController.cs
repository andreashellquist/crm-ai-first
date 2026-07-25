using System.Text.Json;
using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/companies")]
[Authorize]
public class CompaniesController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> List()
    {
        var companies = await db.Companies
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return Ok(companies.Select(ToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CompanyDetailDto>> Get(string id)
    {
        var company = await db.Companies
            .Include(c => c.Contacts.Where(ct => ct.DeletedAt == null))
            .Include(c => c.Deals).ThenInclude(d => d.Stage)
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null);
        if (company is null) return NotFound();

        return Ok(new CompanyDetailDto(
            company.Id,
            company.Name,
            company.Domain,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(company.CustomFields) ?? [],
            company.Contacts.Select(c => new ContactOptionDto(c.Id, string.Join(" ", new[] { c.FirstName, c.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))))).ToList(),
            company.Deals.Where(d => d.DeletedAt == null).Select(d => new CompanyDetailDealDto(d.Id, d.Stage!.Name, d.AmountCents, d.Currency)).ToList()
        ));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CompanyDto>> Update(string id, UpdateCompanyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null);
        if (company is null) return NotFound();

        var fieldDefs = await db.FieldDefinitions
            .Where(f => f.WorkspaceId == current.WorkspaceId && f.EntityType == "company")
            .ToListAsync();
        string customFields;
        try
        {
            customFields = CustomFieldValidator.ValidateAndSerialize(fieldDefs, request.CustomFields);
        }
        catch (CustomFieldValidationException ex)
        {
            return BadRequest(ex.Message);
        }

        company.Name = request.Name;
        company.Domain = string.IsNullOrWhiteSpace(request.Domain) ? null : request.Domain;
        company.CustomFields = customFields;
        company.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(ToDto(company));
    }

    private static CompanyDto ToDto(Company c) => new(
        c.Id, c.Name, c.Domain,
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.CustomFields) ?? []);
}
