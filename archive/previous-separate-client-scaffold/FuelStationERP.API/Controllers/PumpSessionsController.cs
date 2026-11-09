using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using FuelStationERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/branches/{branchId:long}/pump-sessions")]
public sealed class PumpSessionsController(ErpDbContext db, FifoInventoryService inventory) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(long branchId, [FromBody] PumpSessionRequest request, CancellationToken ct)
    {
        if (!CanOperateBranch(branchId)) return Forbid();
        var clientOperationId = string.IsNullOrWhiteSpace(request.ClientOperationId) ? null : request.ClientOperationId.Trim();
        if (clientOperationId is not null)
        {
            var prior = await db.PumpSessions.Where(x => x.BranchId == branchId && x.ClientOperationId == clientOperationId)
                .Select(x => new { x.Id, x.LitresSold, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.RttCost, x.ShortOver })
                .SingleOrDefaultAsync(ct);
            if (prior is not null) return Ok(prior);
        }

        var pump = await db.Pumps.SingleOrDefaultAsync(x => x.Id == request.PumpId && x.BranchId == branchId && x.IsActive, ct);
        if (pump is null) return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["pumpId"] = ["Select an active pump in this branch."] }));
        var tank = await db.Tanks.SingleOrDefaultAsync(x => x.Id == pump.TankId && x.BranchId == branchId && x.IsActive, ct);
        if (tank is null) return Conflict(new { error = "The selected pump has no active tank in this branch." });
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == tank.ProductId && x.IsActive, ct);
        if (product is null) return Conflict(new { error = "The pump tank has no active fuel product." });
        if (!await db.PumpRuns.AnyAsync(x => x.Id == request.RunId &&
                x.BranchId == branchId && x.PumpId == pump.Id && x.TankId == tank.Id && x.Status == "OPEN" &&
                x.ProductCodeSnapshot == product.Code, ct))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["runId"] = ["Select an open product run for this pump and assigned tank."] }));
        if (!await db.PumpRunReceipts.AnyAsync(x => x.PumpRunId == request.RunId &&
                db.FuelReceipts.Any(r => r.Id == x.FuelReceiptId && r.BranchId == branchId && r.ProductCodeSnapshot == product.Code), ct))
            return Conflict(new { error = "The open product run must be linked to at least one matching stock receipt before recording sales." });
        if (!await db.StaffAttendants.AnyAsync(x => x.Id == request.AttendantId && x.BranchId == branchId, ct))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["attendantId"] = ["Select an attendant in this branch."] }));

        var session = new PumpSession
        {
            BranchId = branchId, PumpId = pump.Id, AttendantId = request.AttendantId, RunId = request.RunId,
            ProductCodeSnapshot = product.Code, OpeningMeter = request.OpeningMeter, ClosingMeter = request.ClosingMeter,
            ClientOperationId = clientOperationId,
            PricePerLitre = request.PricePerLitre, CashCollected = request.CashCollected, PosAmount = request.PosAmount,
            Expenses = request.Expenses, RttLitres = request.RttLitres, SessionDate = request.SessionDate,
        };

        try
        {
            await inventory.PostPumpSessionAsync(session, ct);
            return CreatedAtAction(nameof(Get), new { branchId, id = session.Id }, new
            {
                session.Id, session.LitresSold, session.SaleAmount, session.CostOfGoodsSold,
                session.GrossProfit, session.RttCost, session.ShortOver,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The receipt stock changed during posting. Refresh and retry the session." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["session"] = [ex.Message] }));
        }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long branchId, long id, CancellationToken ct)
    {
        if (!CanAccessBranch(branchId)) return Forbid();
        var session = await db.PumpSessions.Where(x => x.BranchId == branchId && x.Id == id)
            .Select(x => new { x.Id, x.PumpId, x.AttendantId, x.SessionDate, x.OpeningMeter, x.ClosingMeter,
                x.LitresSold, x.PricePerLitre, x.SaleAmount, x.CashCollected, x.PosAmount, x.Expenses,
                x.RttLitres, x.RttCost, x.ShortOver, x.CostOfGoodsSold, x.GrossProfit, x.AverageCostPerLitre })
            .SingleOrDefaultAsync(ct);
        return session is null ? NotFound() : Ok(session);
    }

    private bool CanAccessBranch(long branchId) => User.IsInRole("OWNER") ||
        User.FindAll("branch_id").Any(c => c.Value == branchId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private bool CanOperateBranch(long branchId) => User.IsInRole("OWNER") || User.FindAll("branch_role").Any(c =>
    {
        var prefix = branchId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
        return c.Value.StartsWith(prefix, StringComparison.Ordinal) &&
            new[] { "STAFF", "MANAGER", "ADMIN" }.Contains(c.Value[prefix.Length..], StringComparer.OrdinalIgnoreCase);
    });
}

public sealed record PumpSessionRequest(
    [property: Range(1, long.MaxValue)] long PumpId,
    [property: StringLength(120)] string? ClientOperationId,
    [property: Range(1, long.MaxValue)] long AttendantId,
    [property: Range(1, long.MaxValue)] long RunId,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal OpeningMeter,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal ClosingMeter,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal PricePerLitre,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal CashCollected,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal PosAmount,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal Expenses,
    [property: Range(typeof(decimal), "0", "999999999999999")] decimal RttLitres,
    DateOnly SessionDate);
