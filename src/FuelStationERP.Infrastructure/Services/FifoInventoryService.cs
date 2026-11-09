using FuelStationERP.Domain.Entities;
using FuelStationERP.Domain.Services;
using FuelStationERP.Infrastructure.Persistence;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.Infrastructure.Services;

/// <summary>Posts pump sessions against receipt batches using the Version 3 FIFO order.</summary>
public sealed class FifoInventoryService(ErpDbContext db)
{
    public async Task<PumpSession> PostPumpSessionAsync(PumpSession session, CancellationToken cancellationToken = default)
    {
        PumpSessionCalculator.Calculate(session);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var batches = await db.FuelReceipts
                .Where(x => x.BranchId == session.BranchId && x.ProductCodeSnapshot == session.ProductCodeSnapshot &&
                            x.ReceiptDate <= session.SessionDate && x.RemainingLitres > 0)
                .OrderBy(x => x.ReceiptDate).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
            var available = batches.Sum(x => x.RemainingLitres);
            if (available < session.LitresSold)
                throw new InvalidOperationException($"Insufficient recorded stock for {session.ProductCodeSnapshot}. Available {available:N2} {session.UnitOfMeasureSnapshot}; required {session.LitresSold:N2} {session.UnitOfMeasureSnapshot}. Record a costed opening receipt before posting.");

            db.PumpSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);

            var remainingToIssue = session.LitresSold;
            var costOfGoodsSold = 0m;
            foreach (var batch in batches)
            {
                if (remainingToIssue <= 0m) break;
                var issue = Math.Min(batch.RemainingLitres, remainingToIssue);
                var unitCost = batch.EffectiveUnitCost ?? batch.UnitCost;
                batch.RemainingLitres -= issue;
                remainingToIssue -= issue;
                costOfGoodsSold += issue * unitCost;
                db.FifoAllocations.Add(new FifoAllocation { PumpSessionId = session.Id, FuelReceiptId = batch.Id, Litres = issue, UnitCost = unitCost });
            }

            session.CostOfGoodsSold = costOfGoodsSold;
            session.AverageCostPerLitre = session.LitresSold > 0 ? costOfGoodsSold / session.LitresSold : AverageCost(batches);
            session.GrossProfit = session.SaleAmount - costOfGoodsSold;
            session.RttCost = session.RttLitres * session.AverageCostPerLitre;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return session;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static decimal AverageCost(IReadOnlyCollection<FuelReceipt> batches)
    {
        var litres = batches.Sum(x => x.RemainingLitres);
        return litres == 0 ? 0 : batches.Sum(x => x.RemainingLitres * (x.EffectiveUnitCost ?? x.UnitCost)) / litres;
    }
}
