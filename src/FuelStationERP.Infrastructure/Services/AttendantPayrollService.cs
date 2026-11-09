using System.Data;
using FuelStationERP.Domain.Entities;
using FuelStationERP.Domain.Services;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.Infrastructure.Services;

public sealed class AttendantPayrollService(ErpDbContext db)
{
    public async Task<AttendantPayrollPreview> PreviewAsync(long branchId, long attendantId, DateOnly asOf, CancellationToken ct = default)
    {
        var attendant = await db.StaffAttendants.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == attendantId && x.BranchId == branchId, ct)
            ?? throw new InvalidOperationException("The selected attendant was not found in this branch.");
        if (attendant.MonthlySalary < 0) throw new InvalidOperationException("Monthly salary cannot be negative.");
        if (attendant.SalaryPaydayDay is not int paydayDay || paydayDay is < 1 or > 31)
            throw new InvalidOperationException("Set a valid monthly salary payday (1-31) for this attendant first.");

        var payments = await db.AttendantSalaryPayments.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.AttendantId == attendantId)
            .ToListAsync(ct);
        var lastPayday = payments.OrderByDescending(x => x.Payday).ThenByDescending(x => x.Id).Select(x => (DateOnly?)x.Payday).FirstOrDefault();
        var period = AttendantPayrollCalculator.Schedule(asOf, paydayDay, lastPayday, attendant.DateJoined);

        var sessions = await db.PumpSessions.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.AttendantId == attendantId && x.SessionDate <= asOf)
            .OrderBy(x => x.SessionDate).ThenBy(x => x.Id)
            .ToListAsync(ct);
        var sessionIds = sessions.Select(x => x.Id).ToArray();

        var reducedBySession = new Dictionary<long, decimal>();
        if (sessionIds.Length > 0)
        {
            var reconciliations = await db.AttendantShortReconciliations.AsNoTracking()
                .Where(x => x.BranchId == branchId && x.AttendantId == attendantId && x.RecoveryDate <= asOf)
                .ToListAsync(ct);
            var reconciliationIds = reconciliations.Select(x => x.Id).ToArray();
            if (reconciliationIds.Length > 0)
            {
                var allocations = await db.AttendantShortAllocations.AsNoTracking()
                    .Where(x => reconciliationIds.Contains(x.ReconciliationId) && sessionIds.Contains(x.PumpSessionId))
                    .ToListAsync(ct);
                reducedBySession = allocations.GroupBy(x => x.PumpSessionId).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
            }
        }

        var outstandingShort = sessions.Sum(x => Math.Max(Math.Max(-x.ShortOver, 0m) - reducedBySession.GetValueOrDefault(x.Id), 0m));
        var priorPayments = payments.Where(x => x.PaidDate <= asOf).ToList();
        var priorPaymentIds = priorPayments.Select(x => x.Id).ToArray();
        var usedOverages = new Dictionary<long, decimal>();
        if (priorPaymentIds.Length > 0)
        {
            var allocations = await db.AttendantSalaryOverageAllocations.AsNoTracking()
                .Where(x => priorPaymentIds.Contains(x.PaymentId))
                .ToListAsync(ct);
            usedOverages = allocations.GroupBy(x => x.PumpSessionId).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
        }

        var eligibleOverages = sessions
            .Where(x => x.SessionDate >= period.PeriodStart && x.ShortOver > 0)
            .Select(x => new PayrollOverage(x.Id, Math.Max(x.ShortOver - usedOverages.GetValueOrDefault(x.Id), 0m)))
            .Where(x => x.Amount > 0)
            .ToList();

        return AttendantPayrollCalculator.Calculate(period, asOf, attendant.MonthlySalary, outstandingShort, eligibleOverages);
    }

    public async Task<AttendantSalaryPayment> ProcessPaymentAsync(
        long branchId, long attendantId, long accountId, DateOnly paymentDate, string? notes, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var preview = await PreviewAsync(branchId, attendantId, paymentDate, ct);
        if (!preview.Period.IsDue)
            throw new InvalidOperationException($"Salary is not due until {preview.Period.Payday:dd-MM-yyyy}.");
        if (await db.AttendantSalaryPayments.AnyAsync(x => x.BranchId == branchId && x.AttendantId == attendantId && x.Payday == preview.Period.Payday, ct))
            throw new InvalidOperationException("Salary for this payday has already been processed.");

        var attendant = await db.StaffAttendants.SingleAsync(x => x.Id == attendantId && x.BranchId == branchId, ct);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId && x.BranchId == branchId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Choose an active payment account in this branch.");
        if (account.AccountType is not ("CASH" or "BANK"))
            throw new InvalidOperationException("Payroll can only be paid from a Cash or Bank account.");

        var existingTransactions = await db.AccountTransactions.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(ct);
        var balance = AccountBalanceCalculator.Calculate(account, existingTransactions, paymentDate);
        if (preview.NetSalary > balance + 0.005m)
            throw new InvalidOperationException($"The salary payment exceeds the selected account balance of ₦{balance:N2}.");

        var payment = new AttendantSalaryPayment
        {
            BranchId = branchId,
            AttendantId = attendantId,
            Payday = preview.Period.Payday,
            PaidDate = paymentDate,
            BaseSalary = preview.BaseSalary,
            ShortDeduction = preview.ShortDeduction,
            OverageOffset = preview.OverageOffset,
            OverageCredit = preview.OverageCredit,
            NetSalary = preview.NetSalary,
            AccountId = account.Id,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        db.AttendantSalaryPayments.Add(payment);
        await db.SaveChangesAsync(ct);

        await RecordShortReductionAsync(branchId, attendantId, preview.OutstandingShort, preview.OverageOffset, preview.ShortDeduction, paymentDate, payment.Notes, ct);
        AddOverageAllocations(payment.Id, preview);

        var occurredAt = LocalMidnight(paymentDate);
        var accountTransaction = new AccountTransaction
        {
            BranchId = branchId,
            AccountId = account.Id,
            TransactionType = account.AccountType == "CASH" ? "Cash Expense" : "Account Expense",
            Amount = payment.NetSalary,
            Reference = "Payroll",
            Description = $"Salary payment · {attendant.Name} · payday {payment.Payday:yyyy-MM-dd}",
            OccurredAt = occurredAt
        };
        db.AccountTransactions.Add(accountTransaction);
        await db.SaveChangesAsync(ct);

        var expense = new Expense
        {
            BranchId = branchId,
            Description = $"Salary payment · {attendant.Name}",
            Category = "Payroll",
            Amount = payment.NetSalary,
            ExpenseAt = occurredAt,
            Notes = payment.Notes,
            SourceAccountId = account.Id,
            AccountTransactionId = accountTransaction.Id
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);

        payment.AccountTransactionId = accountTransaction.Id;
        payment.ExpenseId = expense.Id;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return payment;
    }

    private async Task RecordShortReductionAsync(
        long branchId, long attendantId, decimal outstandingShort, decimal offset, decimal deduction,
        DateOnly payday, string? notes, CancellationToken ct)
    {
        var amount = offset + deduction;
        if (amount <= 0.005m) return;

        var sessions = await db.PumpSessions.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.AttendantId == attendantId && x.ShortOver < 0 && x.SessionDate <= payday)
            .OrderBy(x => x.SessionDate).ThenBy(x => x.Id)
            .ToListAsync(ct);
        var ids = sessions.Select(x => x.Id).ToArray();
        var priorReductions = new Dictionary<long, decimal>();
        if (ids.Length > 0)
        {
            var reconciliationIds = await db.AttendantShortReconciliations
                .Where(x => x.BranchId == branchId && x.AttendantId == attendantId && x.RecoveryDate <= payday)
                .Select(x => x.Id).ToArrayAsync(ct);
            if (reconciliationIds.Length > 0)
            {
                var allocations = await db.AttendantShortAllocations
                    .Where(x => reconciliationIds.Contains(x.ReconciliationId) && ids.Contains(x.PumpSessionId))
                    .ToListAsync(ct);
                priorReductions = allocations.GroupBy(x => x.PumpSessionId).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
            }
        }

        if (amount > outstandingShort + 0.005m)
            throw new InvalidOperationException("Outstanding short balances changed while processing salary.");

        var reconciliation = new AttendantShortReconciliation
        {
            BranchId = branchId,
            AttendantId = attendantId,
            ReducedAmount = amount,
            RecoveredAmount = 0,
            RecoveryDate = payday,
            Notes = $"Salary payout short offset ({offset:N2}) and deduction ({deduction:N2}). {notes}".Trim()
        };
        db.AttendantShortReconciliations.Add(reconciliation);
        await db.SaveChangesAsync(ct);

        var remaining = amount;
        foreach (var session in sessions)
        {
            if (remaining <= 0.005m) break;
            var sessionShort = Math.Max(-session.ShortOver, 0m);
            var available = Math.Max(sessionShort - priorReductions.GetValueOrDefault(session.Id), 0m);
            var allocated = Math.Min(remaining, available);
            if (allocated <= 0) continue;
            db.AttendantShortAllocations.Add(new AttendantShortAllocation
            {
                ReconciliationId = reconciliation.Id,
                PumpSessionId = session.Id,
                Amount = allocated
            });
            remaining -= allocated;
        }
        if (remaining > 0.005m) throw new InvalidOperationException("Outstanding short balances changed while allocating salary deductions.");
    }

    private void AddOverageAllocations(long paymentId, AttendantPayrollPreview preview)
    {
        var offsetsRemaining = preview.OverageOffset;
        var offsets = new Dictionary<long, decimal>();
        foreach (var overage in preview.EligibleOverages)
        {
            if (offsetsRemaining <= 0.005m) break;
            var amount = Math.Min(offsetsRemaining, overage.Amount);
            if (amount <= 0) continue;
            db.AttendantSalaryOverageAllocations.Add(new AttendantSalaryOverageAllocation
            {
                PaymentId = paymentId,
                PumpSessionId = overage.SessionId,
                AllocationType = "SHORT_OFFSET",
                Amount = amount
            });
            offsets[overage.SessionId] = amount;
            offsetsRemaining -= amount;
        }

        var creditRemaining = preview.OverageCredit;
        foreach (var overage in preview.EligibleOverages)
        {
            if (creditRemaining <= 0.005m) break;
            var available = overage.Amount - offsets.GetValueOrDefault(overage.SessionId);
            var amount = Math.Min(creditRemaining, available);
            if (amount <= 0) continue;
            db.AttendantSalaryOverageAllocations.Add(new AttendantSalaryOverageAllocation
            {
                PaymentId = paymentId,
                PumpSessionId = overage.SessionId,
                AllocationType = "SALARY_CREDIT",
                Amount = amount
            });
            creditRemaining -= amount;
        }

        if (offsetsRemaining > 0.005m || creditRemaining > 0.005m)
            throw new InvalidOperationException("Session overage balances changed while processing salary.");
    }

    private static DateTimeOffset LocalMidnight(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
