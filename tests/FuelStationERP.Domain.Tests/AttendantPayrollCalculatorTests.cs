using FuelStationERP.Domain.Services;
using Xunit;

namespace FuelStationERP.Domain.Tests;

public sealed class AttendantPayrollCalculatorTests
{
    [Fact]
    public void Schedule_ClampsPaydayToLastDayOfMonth()
    {
        var period = AttendantPayrollCalculator.Schedule(new DateOnly(2026, 2, 20), 31, null, new DateOnly(2025, 1, 1));

        Assert.Equal(new DateOnly(2026, 2, 28), period.Payday);
        Assert.Equal(new DateOnly(2025, 1, 1), period.PeriodStart);
        Assert.False(period.IsDue);
    }

    [Fact]
    public void Schedule_AdvancesToMonthAfterLastPaidPayday()
    {
        var period = AttendantPayrollCalculator.Schedule(new DateOnly(2026, 2, 20), 31, new DateOnly(2026, 1, 31), null);

        Assert.Equal(new DateOnly(2026, 2, 28), period.Payday);
        Assert.Equal(new DateOnly(2026, 2, 1), period.PeriodStart);
        Assert.False(period.IsDue);
    }

    [Fact]
    public void Calculate_OffsetsOverageAgainstShortBeforeSalaryDeduction()
    {
        var period = new PayrollPeriod(new DateOnly(2026, 4, 25), new DateOnly(2026, 3, 26), true);

        var preview = AttendantPayrollCalculator.Calculate(
            period,
            new DateOnly(2026, 4, 25),
            baseSalary: 100_000m,
            outstandingShort: 30_000m,
            [new PayrollOverage(1, 10_000m), new PayrollOverage(2, 5_000m)]);

        Assert.Equal(15_000m, preview.OverageOffset);
        Assert.Equal(15_000m, preview.ShortDeduction);
        Assert.Equal(0m, preview.OverageCredit);
        Assert.Equal(85_000m, preview.NetSalary);
    }

    [Fact]
    public void Calculate_CreditsOverageAfterClearingShort()
    {
        var period = new PayrollPeriod(new DateOnly(2026, 4, 25), new DateOnly(2026, 3, 26), true);

        var preview = AttendantPayrollCalculator.Calculate(
            period,
            new DateOnly(2026, 4, 25),
            baseSalary: 100_000m,
            outstandingShort: 10_000m,
            [new PayrollOverage(1, 25_000m)]);

        Assert.Equal(10_000m, preview.OverageOffset);
        Assert.Equal(0m, preview.ShortDeduction);
        Assert.Equal(15_000m, preview.OverageCredit);
        Assert.Equal(115_000m, preview.NetSalary);
    }
}
