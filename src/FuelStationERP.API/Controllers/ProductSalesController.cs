using System.ComponentModel.DataAnnotations;
using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using FuelStationERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/branches/{branchId:long}/product-sales")]
public sealed class ProductSalesController(ErpDbContext db, ProductSaleInventoryService inventory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        return Ok(await db.ProductSales.AsNoTracking().Where(x => x.BranchId == branchId).OrderByDescending(x => x.SaleDate).ThenByDescending(x => x.Id).Take(200).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Post(long branchId, [FromBody] ProductSaleRequest request, CancellationToken ct)
    {
        if (!CanOperate(branchId)) return Forbid();
        var tank = await db.Tanks.SingleOrDefaultAsync(x => x.Id == request.TankId && x.BranchId == branchId && x.IsActive, ct);
        if (tank is null) return BadRequest("Choose an active tank in this branch.");
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == tank.ProductId && x.IsActive, ct);
        if (product is null || product.IsMetered) return BadRequest("Choose a tank for an active non-metered product such as LPG.");
        var effectivePrice = await db.ProductPrices.Where(x => x.ProductId == product.Id && x.EffectiveDate <= request.SaleDate)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id).Select(x => (decimal?)x.UnitPrice).FirstOrDefaultAsync(ct);
        if (effectivePrice is null) return Conflict(new { error = $"No {product.Code} selling price is effective on {request.SaleDate:yyyy-MM-dd}. Add a price in the price book." });
        if (request.AttendantId is long attendantId && !await db.StaffAttendants.AnyAsync(x => x.Id == attendantId && x.BranchId == branchId, ct)) return BadRequest("Choose an attendant in this branch.");
        var sale = new ProductSale { BranchId = branchId, TankId = tank.Id, AttendantId = request.AttendantId, ProductCodeSnapshot = product.Code, UnitOfMeasureSnapshot = product.UnitOfMeasure, Quantity = request.Quantity, UnitPrice = effectivePrice.Value, CashCollected = request.CashCollected, PosAmount = request.PosAmount, Expenses = request.Expenses, CylinderCount = request.CylinderCount, CylinderSize = request.CylinderSize, SaleDate = request.SaleDate };
        try { await inventory.PostAsync(sale, ct); return Created($"/api/v1/branches/{branchId}/product-sales/{sale.Id}", new { sale.Id, sale.Quantity, sale.SaleAmount, sale.ShortOver, sale.CostOfGoodsSold, sale.GrossProfit }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Stock changed while this sale was posting. Refresh and try again." }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private bool CanAccess(long branchId) => User.IsInRole("OWNER") || User.FindAll("branch_id").Any(c => c.Value == branchId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private bool CanOperate(long branchId) => User.IsInRole("OWNER") || User.FindAll("branch_role").Any(c => c.Value == $"{branchId}:STAFF" || c.Value == $"{branchId}:MANAGER" || c.Value == $"{branchId}:ADMIN");
}

public sealed record ProductSaleRequest([property: Range(1, long.MaxValue)] long TankId, long? AttendantId,
    [property: Range(typeof(decimal), "0.0001", "999999999999999")] decimal Quantity,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal UnitPrice,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal CashCollected,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal PosAmount,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal Expenses,
    int? CylinderCount, string? CylinderSize, DateOnly SaleDate);
