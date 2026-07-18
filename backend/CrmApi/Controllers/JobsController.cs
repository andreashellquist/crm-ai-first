using CrmApi.Data;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize]
public class JobsController(AppDbContext db, CurrentUser current) : ControllerBase
{
    [HttpGet("{jobId}")]
    public async Task<ActionResult<JobStatusResponse>> GetStatus(string jobId)
    {
        // Re-scoped by workspace so a caller can never read another
        // workspace's job status.
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.WorkspaceId == current.WorkspaceId);
        if (job is null) return NotFound();
        return Ok(new JobStatusResponse(job.Status, job.LastError));
    }
}
