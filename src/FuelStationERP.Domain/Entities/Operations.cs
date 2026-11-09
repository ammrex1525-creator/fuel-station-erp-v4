namespace FuelStationERP.Domain.Entities;

public interface IBranchEntity { long BranchId { get; set; } }

public sealed class Branch { public long Id { get; set; } public string Name { get; set; } = ""; public string Code { get; set; } = ""; public bool IsActive { get; set; } = true; }
public sealed class UserBranchMembership { public long Id { get; set; } public string UserId { get; set; } = ""; public long BranchId { get; set; } public string RoleName { get; set; } = "STAFF"; public bool IsActive { get; set; } = true; }
public sealed class FuelProduct { public long Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string UnitOfMeasure { get; set; } = "L"; public bool IsMetered { get; set; } = true; public bool IsActive { get; set; } = true; }
/// <summary>Effective-dated selling price; the price in effect on the session/sale date is applied automatically.</summary>
public sealed class ProductPrice { public long Id { get; set; } public long ProductId { get; set; } public decimal UnitPrice { get; set; } public DateOnly EffectiveDate { get; set; } }
public sealed class Supplier : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string Name { get; set; } = ""; public string? Phone { get; set; } }
public sealed class Account : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string Name { get; set; } = ""; public string AccountType { get; set; } = "CASH"; public string? AccountNumber { get; set; } public string? Provider { get; set; } public bool IsActive { get; set; } = true; }
public sealed class Tank : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string Number { get; set; } = ""; public long ProductId { get; set; } public decimal CapacityLitres { get; set; } public bool IsActive { get; set; } = true; }
public sealed class Pump : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string Number { get; set; } = ""; public long TankId { get; set; } public bool IsActive { get; set; } = true; }
public sealed class FuelReceipt : IBranchEntity
{
    public long Id { get; set; } public long BranchId { get; set; } public long TankId { get; set; } public long? SupplierId { get; set; } public string? SupplierNameSnapshot { get; set; }
    public string? InvoiceNumber { get; set; } public string? DeliveryNote { get; set; } public string ProductCodeSnapshot { get; set; } = "";
    public string UnitOfMeasureSnapshot { get; set; } = "L"; public string? VehicleNumber { get; set; } public string? DriverName { get; set; } public string? DriverPhone { get; set; }
    public decimal InvoiceLitres { get; set; } public decimal ReceivedLitres { get; set; } public decimal RemainingLitres { get; set; }
    public decimal UnitCost { get; set; } public decimal? EffectiveUnitCost { get; set; } public decimal TotalCost { get; set; }
    public decimal ShortOverLitres { get; set; } public decimal VarianceValue { get; set; } public DateOnly ReceiptDate { get; set; }
    public string ReceiptType { get; set; } = "Delivery"; public byte[] RowVersion { get; set; } = [];
}
public sealed class PumpRun : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public long PumpId { get; set; } public long TankId { get; set; } public string ProductCodeSnapshot { get; set; } = ""; public decimal? OpeningMeter { get; set; } public decimal? ClosingMeter { get; set; } public string Status { get; set; } = "Open"; public DateTimeOffset StartedAt { get; set; } public DateTimeOffset? FinishedAt { get; set; } }
public sealed class PumpSession : IBranchEntity
{
    public long Id { get; set; } public long BranchId { get; set; } public long PumpId { get; set; } public long? AttendantId { get; set; } public long? RunId { get; set; } public string ProductCodeSnapshot { get; set; } = ""; public string? ClientOperationId { get; set; }
    public string UnitOfMeasureSnapshot { get; set; } = "L";
    public decimal OpeningMeter { get; set; } public decimal ClosingMeter { get; set; } public decimal LitresSold { get; set; }
    public decimal PricePerLitre { get; set; } public decimal SaleAmount { get; set; } public decimal CashCollected { get; set; }
    public decimal PosAmount { get; set; } public decimal Expenses { get; set; } public decimal RttAmount { get; set; }
    public decimal RttLitres { get; set; } public decimal RttCost { get; set; } public decimal ShortOver { get; set; }
    public decimal CostOfGoodsSold { get; set; } public decimal GrossProfit { get; set; } public decimal AverageCostPerLitre { get; set; }
    public DateOnly SessionDate { get; set; }
}
/// <summary>Weight or package based sales such as LPG cylinder fills, which do not use a pump meter.</summary>
public sealed class ProductSale : IBranchEntity
{
    public long Id { get; set; } public long BranchId { get; set; } public long TankId { get; set; } public long? AttendantId { get; set; }
    public string ProductCodeSnapshot { get; set; } = ""; public string UnitOfMeasureSnapshot { get; set; } = "kg";
    public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal SaleAmount { get; set; }
    public decimal CashCollected { get; set; } public decimal PosAmount { get; set; } public decimal Expenses { get; set; }
    public decimal ShortOver { get; set; } public decimal CostOfGoodsSold { get; set; } public decimal GrossProfit { get; set; }
    public int? CylinderCount { get; set; } public string? CylinderSize { get; set; } public DateOnly SaleDate { get; set; }
}
public sealed class ProductSaleAllocation { public long Id { get; set; } public long ProductSaleId { get; set; } public long FuelReceiptId { get; set; } public decimal Quantity { get; set; } public decimal UnitCost { get; set; } }
public sealed class FifoAllocation { public long Id { get; set; } public long PumpSessionId { get; set; } public long FuelReceiptId { get; set; } public decimal Litres { get; set; } public decimal UnitCost { get; set; } }
public sealed class TankDip : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public long TankId { get; set; } public decimal OpeningDip { get; set; } public decimal ClosingDip { get; set; } public decimal? ExpectedLitres { get; set; } public decimal? BaselineCost { get; set; } public DateTimeOffset RecordedAt { get; set; } public long? RecordedByUserId { get; set; } public string? Notes { get; set; } }
public sealed class Expense : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string Description { get; set; } = ""; public string? Category { get; set; } public decimal Amount { get; set; } public DateTimeOffset ExpenseAt { get; set; } public string? Notes { get; set; } public long? SourceAccountId { get; set; } public long? AccountTransactionId { get; set; } public long? RecordedByUserId { get; set; } }
public sealed class AccountTransaction : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string TransactionType { get; set; } = ""; public decimal Amount { get; set; } public long? AccountId { get; set; } public long? TargetAccountId { get; set; } public long? FuelReceiptId { get; set; } public long? ShortReconciliationId { get; set; } public string? BankName { get; set; } public string? Reference { get; set; } public string? Description { get; set; } public DateTimeOffset OccurredAt { get; set; } }
public sealed class CashCollection : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public long PumpSessionId { get; set; } public decimal CashAmount { get; set; } public decimal PosAmount { get; set; } public DateTimeOffset CollectedAt { get; set; } public long? CollectedByUserId { get; set; } }
public sealed class AttendantShortReconciliation : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public long AttendantId { get; set; } public decimal ReducedAmount { get; set; } public decimal RecoveredAmount { get; set; } public DateOnly RecoveryDate { get; set; } public string? Notes { get; set; } }
public sealed class ReceiptShortSettlement : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public long FuelReceiptId { get; set; } public string PartyType { get; set; } = ""; public string PartyName { get; set; } = ""; public decimal ReconciledLitres { get; set; } public decimal ReconciledCost { get; set; } public decimal StationBorneLitres { get; set; } public decimal StationBorneCost { get; set; } public bool Disputed { get; set; } public DateOnly SettlementDate { get; set; } }
public sealed class SyncSubmission : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string Source { get; set; } = "Web"; public string SubmissionType { get; set; } = ""; public string PayloadJson { get; set; } = "{}"; public string Status { get; set; } = "Pending"; public DateTimeOffset SubmittedAt { get; set; } public string? ExternalId { get; set; } }
public sealed class StaffAttendant : IBranchEntity { public long Id { get; set; } public long BranchId { get; set; } public string Name { get; set; } = ""; public string? Phone { get; set; } public DateOnly? DateJoined { get; set; } public decimal MonthlySalary { get; set; } public int? SalaryPaydayDay { get; set; } }
public sealed class PumpSessionAudit { public long Id { get; set; } public long PumpSessionId { get; set; } public DateTimeOffset ChangedAt { get; set; } public string? ChangedBy { get; set; } public string Reason { get; set; } = ""; public decimal? OldOpeningMeter { get; set; } public decimal? NewOpeningMeter { get; set; } public decimal? OldClosingMeter { get; set; } public decimal? NewClosingMeter { get; set; } }
public sealed class PumpRunReceipt { public long Id { get; set; } public long PumpRunId { get; set; } public long FuelReceiptId { get; set; } public decimal QuantityReceived { get; set; } public decimal TotalCost { get; set; } public decimal? OpeningMeter { get; set; } }
public sealed class AttendantShortAllocation { public long Id { get; set; } public long ReconciliationId { get; set; } public long PumpSessionId { get; set; } public decimal Amount { get; set; } }
