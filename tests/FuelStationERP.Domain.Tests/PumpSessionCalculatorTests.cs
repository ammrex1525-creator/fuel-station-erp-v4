using FuelStationERP.Domain.Entities;
using FuelStationERP.Domain.Services;
using Xunit;

namespace FuelStationERP.Domain.Tests;

public sealed class PumpSessionCalculatorTests
{
    [Fact]
    public void Calculate_ExcludesRttAndComputesCashShortOver()
    {
        var session = new PumpSession
        {
            OpeningMeter = 100.5m, ClosingMeter = 205.5m, RttLitres = 5m,
            PricePerLitre = 500m, CashCollected = 40_000m, PosAmount = 9_500m, Expenses = 1_000m,
        };

        PumpSessionCalculator.Calculate(session);

        Assert.Equal(100m, session.LitresSold);
        Assert.Equal(50_000m, session.SaleAmount);
        Assert.Equal(500m, session.ShortOver);
    }

    [Fact]
    public void Calculate_RejectsRttAboveMeterMovement()
    {
        var session = new PumpSession { OpeningMeter = 10m, ClosingMeter = 20m, RttLitres = 10.01m };

        Assert.Throws<ArgumentException>(() => PumpSessionCalculator.Calculate(session));
    }

    [Fact]
    public void Calculate_RejectsClosingMeterBelowOpeningMeter()
    {
        var session = new PumpSession { OpeningMeter = 20m, ClosingMeter = 19m };

        Assert.Throws<ArgumentException>(() => PumpSessionCalculator.Calculate(session));
    }
}
