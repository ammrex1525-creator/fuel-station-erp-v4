namespace FuelStationERP.Domain.Services;

public sealed record PayrollPeriod(DateOnly Payday, DateOnly PeriodStart, bool IsDue);
public sealed record PayrollOverage(long SessionId, decimal Amount);
public sealed record AttendantPayrollPreview(
    PayrollPeriod Period,
    decimal BaseSalary,
    decimal OutstandingShort,
    decimal OverageTotal,
    decimal OverageOffset,
    decimal ShortDeduction,
    decimal OverageCredit,
    decimal NetSalary,
    IReadOnlyList<PayrollOverage> EligibleOverages);

public static class AttendantPayrollCalculator
{
    public static PayrollPeriod Schedule(DateOnly asOf, int paydayDay, DateOnly? lastPayday, DateOnly? dateJoined)
    {
        if (paydayDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(paydayDay));

        var year = lastPayday?.Year ?? asOf.Year;
        var month = lastPayday?.Month ?? asOf.Month;
        if (lastPayday is not null)
        {
            if (month == 12) { year++; month = 1; }
            else month++;
        }

        var payday = new DateOnly(year, month, Math.Min(paydayDay, DateTime.DaysInMonth(year, month)));
        var periodStart = lastPayday?.AddDays(1) ?? dateJoined ?? DateOnly.MinValue;
        return new PayrollPeriod(payday, periodStart, payday <= asOf);
    }

    public static AttendantPayrollPreview Calculate(
        PayrollPeriod period,
        DateOnly asOf,
        decimal baseSalary,
        decimal outstandingShort,
        IReadOnlyList<PayrollOverage> eligibleOverages)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseSalary);
        ArgumentOutOfRangeException.ThrowIfNegative(outstandingShort);
        ArgumentNullException.ThrowIfNull(eligibleOverages);
        if (eligibleOverages.Any(x => x.Amount < 0)) throw new ArgumentOutOfRangeException(nameof(eligibleOverages));

        var overageTotal = eligibleOverages.Sum(x => x.Amount);
        var offset = Math.Min(outstandingShort, overageTotal);
        var shortAfterOffset = outstandingShort - offset;
        var deduction = Math.Min(baseSalary, shortAfterOffset);
        var credit = overageTotal - offset;
        var netSalary = Math.Max(baseSalary - deduction + credit, 0m);

        return new AttendantPayrollPreview(period, baseSalary, outstandingShort, overageTotal, offset, deduction, credit, netSalary, eligibleOverages);
    }
}
