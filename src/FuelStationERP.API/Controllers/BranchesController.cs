using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/branches")]
public sealed class BranchesController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var query = db.Branches.AsNoTracking();
        if (!User.IsInRole("OWNER"))
        {
            var ids = User.FindAll("branch_id").Select(c => long.TryParse(c.Value, out var id) ? id : -1).ToArray();
            query = query.Where(x => ids.Contains(x.Id));
        }
        return Ok(await query.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Branch request, CancellationToken ct)
    {
        if (!User.IsInRole("OWNER")) return Forbid();
        request.Id = 0;
        request.Name = request.Name.Trim();
        request.Code = request.Code.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code)) return BadRequest("Branch name and code are required.");
        db.Branches.Add(request);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { id = request.Id }, request);
    }
}
