using FuelStationERP.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace FuelStationERP.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Branch> Branches => Set<Branch>(); public DbSet<FuelProduct> Products => Set<FuelProduct>(); public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<UserBranchMembership> UserBranchMemberships => Set<UserBranchMembership>();
    public DbSet<Supplier> Suppliers => Set<Supplier>(); public DbSet<Account> Accounts => Set<Account>(); public DbSet<Tank> Tanks => Set<Tank>(); public DbSet<Pump> Pumps => Set<Pump>();
    public DbSet<FuelReceipt> FuelReceipts => Set<FuelReceipt>(); public DbSet<PumpRun> PumpRuns => Set<PumpRun>();
    public DbSet<PumpSession> PumpSessions => Set<PumpSession>(); public DbSet<FifoAllocation> FifoAllocations => Set<FifoAllocation>();
    public DbSet<ProductSale> ProductSales => Set<ProductSale>(); public DbSet<ProductSaleAllocation> ProductSaleAllocations => Set<ProductSaleAllocation>();
    public DbSet<TankDip> TankDips => Set<TankDip>(); public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>(); public DbSet<CashCollection> CashCollections => Set<CashCollection>();
    public DbSet<AttendantShortReconciliation> AttendantShortReconciliations => Set<AttendantShortReconciliation>();
    public DbSet<ReceiptShortSettlement> ReceiptShortSettlements => Set<ReceiptShortSettlement>(); public DbSet<SyncSubmission> SyncSubmissions => Set<SyncSubmission>();
    public DbSet<StaffAttendant> StaffAttendants => Set<StaffAttendant>(); public DbSet<PumpSessionAudit> PumpSessionAudits => Set<PumpSessionAudit>();
    public DbSet<PumpRunReceipt> PumpRunReceipts => Set<PumpRunReceipt>(); public DbSet<AttendantShortAllocation> AttendantShortAllocations => Set<AttendantShortAllocation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Branch>().HasIndex(x => x.Code).IsUnique(); b.Entity<FuelProduct>().HasIndex(x => x.Code).IsUnique();
        b.Entity<ProductPrice>().HasIndex(x => new { x.ProductId, x.EffectiveDate }).IsUnique();
        b.Entity<UserBranchMembership>().HasIndex(x => new { x.UserId, x.BranchId }).IsUnique();
        b.Entity<Account>().HasIndex(x => new { x.BranchId, x.AccountType, x.Name }).IsUnique();
        b.Entity<FuelReceipt>().HasIndex(x => new { x.BranchId, x.ProductCodeSnapshot, x.ReceiptDate, x.Id });
        b.Entity<FuelReceipt>().Property(x => x.RowVersion).IsConcurrencyToken();
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
            b.Entity<FuelReceipt>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<SyncSubmission>().HasIndex(x => new { x.BranchId, x.Status, x.SubmittedAt });
        b.Entity<SyncSubmission>().HasIndex(x => new { x.BranchId, x.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
        b.Entity<PumpSession>().HasIndex(x => new { x.BranchId, x.ClientOperationId }).IsUnique().HasFilter("\"ClientOperationId\" IS NOT NULL");
        b.Entity<PumpRunReceipt>().HasIndex(x => new { x.PumpRunId, x.FuelReceiptId }).IsUnique();
        b.Entity<AttendantShortAllocation>().HasIndex(x => new { x.ReconciliationId, x.PumpSessionId }).IsUnique();
        b.Entity<ProductSaleAllocation>().HasIndex(x => new { x.ProductSaleId, x.FuelReceiptId }).IsUnique();
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(IBranchEntity).IsAssignableFrom(t.ClrType)))
        {
            var entity = b.Entity(type.ClrType); entity.Property(nameof(IBranchEntity.BranchId)).IsRequired();
            entity.HasIndex(nameof(IBranchEntity.BranchId));
        }
        b.Entity<PumpSession>().Property(x => x.SessionDate).HasColumnType("date");
        b.Entity<FuelReceipt>().Property(x => x.ReceiptDate).HasColumnType("date");
        b.Entity<AttendantShortReconciliation>().Property(x => x.RecoveryDate).HasColumnType("date");
        b.Entity<ReceiptShortSettlement>().Property(x => x.SettlementDate).HasColumnType("date");
        b.Entity<ProductPrice>().Property(x => x.EffectiveDate).HasColumnType("date");
        foreach (var type in b.Model.GetEntityTypes()) foreach (var property in type.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            b.Entity(type.ClrType).Property(property.Name).HasPrecision(19, 4);
    }
}
