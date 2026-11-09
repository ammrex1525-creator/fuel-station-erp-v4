using FuelStationERP.Domain.Entities;

namespace FuelStationERP.Infrastructure.Services;

public static class AccountBalanceCalculator
{
    public static decimal Calculate(Account account, IReadOnlyCollection<AccountTransaction> all, DateOnly cutoff)
    {
        var items = all.Where(x => DateOnly.FromDateTime(x.OccurredAt.LocalDateTime) <= cutoff && (x.AccountId == account.Id || x.TargetAccountId == account.Id)).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToList();
        var anchor = account.AccountType == "CASH" ? items.LastOrDefault(x => x.AccountId == account.Id && x.TransactionType == "Cash Count") : null;
        decimal balance = anchor?.Amount ?? 0;
        if (anchor is not null) items = items.Where(x => x.OccurredAt.Date > anchor.OccurredAt.Date || (x.OccurredAt.Date == anchor.OccurredAt.Date && x.Id > anchor.Id)).ToList();
        foreach (var item in items)
        {
            if (item.TransactionType == "Cash Count") continue;
            if (item.TargetAccountId == account.Id && item.AccountId != account.Id) { balance += item.Amount; continue; }
            if (item.AccountId != account.Id) continue;
            if (item.TransactionType is "Opening Balance" or "Account Receipt" or "POS Transaction" or "Cash collection" or "POS collection") balance += item.Amount;
            else if (item.TransactionType is "Account Payment / Amount Sent" or "Bank Payment / Withdrawal" or "Cash Expense" or "Account Expense" or "Product Payment" or "Expense") balance -= item.Amount;
            else if (item.TransactionType is "Account Transfer" or "Bank Deposit" or "POS Settlement")
            {
                if (item.TargetAccountId is long target && target != account.Id) balance -= item.Amount;
                else if (item.TargetAccountId is null) balance += item.Amount;
            }
        }
        return balance;
    }
}
