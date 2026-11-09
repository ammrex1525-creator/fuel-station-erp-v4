using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/branches/{branchId:long}/reports")]
public sealed class ReportsController(ErpDbContext db) : ControllerBase
{
    [HttpGet("operations")]
    public async Task<IActionResult> Operations(long branchId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!User.IsInRole("OWNER") && !User.FindAll("branch_id").Any(c => c.Value == branchId.ToString(System.Globalization.CultureInfo.InvariantCulture))) return Forbid();
        var start = from ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-29));
        var end = to ?? DateOnly.FromDateTime(DateTime.Today);
        if (end < start) return BadRequest("The end date must be on or after the start date.");
        if (end.DayNumber - start.DayNumber > 366) return BadRequest("Report periods can cover at most 367 days.");

        var sessions = await db.PumpSessions.AsNoTracking().Where(x => x.BranchId == branchId && x.SessionDate >= start && x.SessionDate <= end)
            .OrderBy(x => x.SessionDate).ThenBy(x => x.Id)
            .Select(x => new ReportLine("Pump session", x.SessionDate, x.ProductCodeSnapshot, x.LitresSold, x.UnitOfMeasureSnapshot, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.ShortOver))
            .ToListAsync(ct);
        var fills = await db.ProductSales.AsNoTracking().Where(x => x.BranchId == branchId && x.SaleDate >= start && x.SaleDate <= end)
            .OrderBy(x => x.SaleDate).ThenBy(x => x.Id)
            .Select(x => new ReportLine("Product sale", x.SaleDate, x.ProductCodeSnapshot, x.Quantity, x.UnitOfMeasureSnapshot, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.ShortOver))
            .ToListAsync(ct);
        var localZone = TimeZoneInfo.Local;
        var startLocal = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var expenseStart = new DateTimeOffset(startLocal, localZone.GetUtcOffset(startLocal));
        var expenseEnd = new DateTimeOffset(endLocal, localZone.GetUtcOffset(endLocal));
        var expenseQuery = db.Expenses.AsNoTracking().Where(x => x.BranchId == branchId);
        var expenseRows = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? await expenseQuery.ToListAsync(ct)
            : await expenseQuery.Where(x => x.ExpenseAt >= expenseStart && x.ExpenseAt < expenseEnd).ToListAsync(ct);
        var expenses = expenseRows.Where(x =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpenseAt, localZone).DateTime) >= start &&
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpenseAt, localZone).DateTime) <= end)
            .OrderBy(x => x.ExpenseAt).ThenBy(x => x.Id)
            .Select(x => new ReportLine("Expense", DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.ExpenseAt, localZone).DateTime), x.Category ?? x.Description, 0, "", -x.Amount, 0, -x.Amount, 0)).ToList();
        var receipts = await db.FuelReceipts.AsNoTracking().Where(x => x.BranchId == branchId && x.ReceiptDate >= start && x.ReceiptDate <= end).OrderBy(x => x.ReceiptDate).ThenBy(x => x.Id).ToListAsync(ct);
        var deliveries = receipts.Select(x => new ReportLine("Fuel delivery", x.ReceiptDate, $"{x.ProductCodeSnapshot} · {x.SupplierNameSnapshot ?? "Supplier not set"} · Invoice {x.InvoiceNumber ?? "—"}", x.ReceivedLitres, x.UnitOfMeasureSnapshot, 0, x.TotalCost, 0, Math.Max(x.InvoiceLitres - x.ReceivedLitres, 0))).ToList();
        var rtt = await db.PumpSessions.AsNoTracking().Where(x => x.BranchId == branchId && x.SessionDate >= start && x.SessionDate <= end && x.RttLitres > 0)
            .Select(x => new ReportLine("RTT returned", x.SessionDate, x.ProductCodeSnapshot, x.RttLitres, x.UnitOfMeasureSnapshot, 0, x.RttCost, -x.RttCost, 0)).ToListAsync(ct);
        var tanks = await db.Tanks.AsNoTracking().Where(x => x.BranchId == branchId).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
        var dipEntities = await db.TankDips.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(ct);
        var stock = dipEntities.Where(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.RecordedAt, localZone).DateTime) >= start && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.RecordedAt, localZone).DateTime) <= end)
            .Select(x => new ReportLine(x.Notes != null && x.Notes.Contains("opening baseline", StringComparison.OrdinalIgnoreCase) ? "Opening baseline" : "Tank stock check", DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.RecordedAt, localZone).DateTime), $"Tank {tanks.GetValueOrDefault(x.TankId, x.TankId.ToString())} · {(x.Notes?.Contains("opening baseline", StringComparison.OrdinalIgnoreCase) == true ? "baseline cost excluded from profit" : "expected closing − physical closing")}", x.ClosingDip, "Product unit", x.BaselineCost ?? 0, x.ExpectedLitres ?? 0, x.Notes?.Contains("opening baseline", StringComparison.OrdinalIgnoreCase) == true ? 0 : (x.ExpectedLitres ?? 0) - x.ClosingDip, 0)).ToList();
        var staff = await db.StaffAttendants.AsNoTracking().Where(x => x.BranchId == branchId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var attendantRows = (await db.PumpSessions.AsNoTracking().Where(x => x.BranchId == branchId && x.SessionDate >= start && x.SessionDate <= end && x.AttendantId != null).ToListAsync(ct))
            .Select(x => new ReportLine("Attendant activity", x.SessionDate, $"{staff.GetValueOrDefault(x.AttendantId!.Value, "Staff")} · {x.ProductCodeSnapshot}", x.LitresSold, x.UnitOfMeasureSnapshot, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.ShortOver)).ToList();
        attendantRows.AddRange((await db.ProductSales.AsNoTracking().Where(x => x.BranchId == branchId && x.SaleDate >= start && x.SaleDate <= end && x.AttendantId != null).ToListAsync(ct))
            .Select(x => new ReportLine("Attendant activity", x.SaleDate, $"{staff.GetValueOrDefault(x.AttendantId!.Value, "Staff")} · {x.ProductCodeSnapshot}", x.Quantity, x.UnitOfMeasureSnapshot, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.ShortOver)));
        var lines = sessions.Concat(fills).Concat(expenses).Concat(deliveries).Concat(rtt).Concat(stock).Concat(attendantRows).OrderBy(x => x.Date).ThenBy(x => x.Type, StringComparer.Ordinal).ToList();
        return Ok(new
        {
            branchId, from = start, to = end, count = lines.Count,
            totals = new
            {
                sales = sessions.Sum(x => x.Sales) + fills.Sum(x => x.Sales),
                costOfGoodsSold = sessions.Sum(x => x.CostOfGoodsSold) + fills.Sum(x => x.CostOfGoodsSold),
                grossProfit = sessions.Sum(x => x.GrossProfit) + fills.Sum(x => x.GrossProfit),
                expenses = expenses.Sum(x => -x.Sales),
                netOperatingProfit = sessions.Sum(x => x.GrossProfit) + fills.Sum(x => x.GrossProfit) - expenses.Sum(x => -x.Sales),
                shortOver = sessions.Sum(x => x.ShortOver) + fills.Sum(x => x.ShortOver)
            },
            rows = lines
        });
    }
}

public sealed record ReportLine(string Type, DateOnly Date, string Product, decimal Quantity, string Unit,
    decimal Sales, decimal CostOfGoodsSold, decimal GrossProfit, decimal ShortOver);
