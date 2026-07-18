using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Models;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/contacts")]
[Authorize]
public class ContactsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ContactDto>>> List()
    {
        var contacts = await db.Contacts
            .Include(c => c.Company)
            .Where(c => c.WorkspaceId == current.WorkspaceId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ContactDto(c.Id, c.FirstName, c.LastName, c.Email, c.Company != null ? c.Company.Name : null, c.LifecycleStage))
            .ToListAsync();
        return Ok(contacts);
    }

    [HttpPost]
    public async Task<ActionResult<ContactDto>> Create(CreateContactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
            return BadRequest("First name is required");

        string? companyId = null;
        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            var company = await db.Companies.FirstOrDefaultAsync(
                c => c.WorkspaceId == current.WorkspaceId && c.Name == request.CompanyName);
            if (company is null)
            {
                company = new Company { WorkspaceId = current.WorkspaceId, Name = request.CompanyName };
                db.Companies.Add(company);
                await db.SaveChangesAsync();
            }
            companyId = company.Id;
        }

        var contact = new Contact
        {
            WorkspaceId = current.WorkspaceId,
            FirstName = request.FirstName,
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email,
            CompanyId = companyId,
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        return Ok(new ContactDto(contact.Id, contact.FirstName, contact.LastName, contact.Email, request.CompanyName, contact.LifecycleStage));
    }
}
