using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/branches/{branchId:long}/dashboard")]
public sealed class DashboardController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long branchId, DateOnly? date, CancellationToken ct)
    {
        if (!User.IsInRole("OWNER") && !User.FindAll("branch_id").Any(c => c.Value == branchId.ToString(System.Globalization.CultureInfo.InvariantCulture))) return Forbid();
        var day = date ?? DateOnly.FromDateTime(DateTime.Now);
        var sessions = db.PumpSessions.AsNoTracking().Where(x => x.BranchId == branchId && x.SessionDate == day);
        var sales = await sessions.SumAsync(x => (decimal?)x.SaleAmount, ct) ?? 0m;
        var cogs = await sessions.SumAsync(x => (decimal?)x.CostOfGoodsSold, ct) ?? 0m;
        var grossProfit = await sessions.SumAsync(x => (decimal?)x.GrossProfit, ct) ?? 0m;
        var shortOver = await sessions.SumAsync(x => (decimal?)x.ShortOver, ct) ?? 0m;
        var productRows = db.ProductSales.AsNoTracking().Where(x => x.BranchId == branchId && x.SaleDate == day);
        var productSales = await productRows.SumAsync(x => (decimal?)x.SaleAmount, ct) ?? 0m;
        cogs += await productRows.SumAsync(x => (decimal?)x.CostOfGoodsSold, ct) ?? 0m;
        grossProfit += await productRows.SumAsync(x => (decimal?)x.GrossProfit, ct) ?? 0m;
        shortOver += await productRows.SumAsync(x => (decimal?)x.ShortOver, ct) ?? 0m;
        var zone = TimeZoneInfo.Local; var localStart = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified); var localEnd = DateTime.SpecifyKind(day.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var start = new DateTimeOffset(localStart, zone.GetUtcOffset(localStart)); var end = new DateTimeOffset(localEnd, zone.GetUtcOffset(localEnd));
        decimal expenses;
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var localExpenses = await db.Expenses.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(ct);
            expenses = localExpenses.Where(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpenseAt, zone).DateTime) == day).Sum(x => x.Amount);
        }
        else expenses = await db.Expenses.AsNoTracking().Where(x => x.BranchId == branchId && x.ExpenseAt >= start && x.ExpenseAt < end).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var recent = await sessions.OrderByDescending(x => x.Id).Take(8).Select(x => new { x.Id, x.PumpId, x.SessionDate, x.LitresSold, x.SaleAmount, x.ShortOver }).ToListAsync(ct);
        return Ok(new { branchId, date = day, fuelSales = sales, otherProductSales = productSales, totalSales = sales + productSales, cogs, grossProfit, expenses, netOperatingProfit = grossProfit - expenses, shortOver, recentSessions = recent });
    }
}
