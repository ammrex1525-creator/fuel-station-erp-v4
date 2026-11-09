using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace FuelStationERP.Infrastructure.Services;

/// <summary>Posts non-metered product sales, such as LPG fills, against the oldest matching receipt batches.</summary>
public sealed class ProductSaleInventoryService(ErpDbContext db)
{
    public async Task<ProductSale> PostAsync(ProductSale sale, CancellationToken ct = default)
    {
        if (sale.Quantity <= 0 || sale.UnitPrice < 0 || sale.CashCollected < 0 || sale.PosAmount < 0 || sale.Expenses < 0)
            throw new ArgumentException("Quantity must be positive and price, collections and expenses cannot be negative.");
        sale.SaleAmount = sale.Quantity * sale.UnitPrice;
        sale.ShortOver = sale.CashCollected + sale.PosAmount - (sale.SaleAmount - sale.Expenses);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var batches = await db.FuelReceipts.Where(x => x.BranchId == sale.BranchId && x.ProductCodeSnapshot == sale.ProductCodeSnapshot && x.ReceiptDate <= sale.SaleDate && x.RemainingLitres > 0)
                .OrderBy(x => x.ReceiptDate).ThenBy(x => x.Id).ToListAsync(ct);
            var available = batches.Sum(x => x.RemainingLitres);
            if (available < sale.Quantity) throw new InvalidOperationException($"Insufficient recorded stock for {sale.ProductCodeSnapshot}. Available {available:N2} {sale.UnitOfMeasureSnapshot}; required {sale.Quantity:N2} {sale.UnitOfMeasureSnapshot}.");
            db.ProductSales.Add(sale); await db.SaveChangesAsync(ct);
            var left = sale.Quantity; var cost = 0m;
            foreach (var batch in batches)
            {
                if (left <= 0) break;
                var issue = Math.Min(left, batch.RemainingLitres); var unitCost = batch.EffectiveUnitCost ?? batch.UnitCost;
                batch.RemainingLitres -= issue; left -= issue; cost += issue * unitCost;
                db.ProductSaleAllocations.Add(new ProductSaleAllocation { ProductSaleId = sale.Id, FuelReceiptId = batch.Id, Quantity = issue, UnitCost = unitCost });
            }
            sale.CostOfGoodsSold = cost; sale.GrossProfit = sale.SaleAmount - cost;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return sale;
        }
        catch { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); throw; }
    }
}
