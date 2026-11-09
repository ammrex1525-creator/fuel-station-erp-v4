using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/products")]
public sealed class ProductsController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.Products.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FuelProduct request, CancellationToken ct)
    {
        if (!User.IsInRole("OWNER") && !User.IsInRole("ADMIN")) return Forbid();
        request.Id = 0;
        request.Code = request.Code.Trim().ToUpperInvariant();
        request.Name = request.Name.Trim();
        request.UnitOfMeasure = request.UnitOfMeasure.Trim();
        if (request.Code.Length == 0 || request.Name.Length == 0 || request.UnitOfMeasure.Length == 0) return BadRequest("Code, name and unit are required.");
        db.Products.Add(request);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "A product with this code already exists." }); }
        return CreatedAtAction(nameof(List), new { id = request.Id }, request);
    }
}
