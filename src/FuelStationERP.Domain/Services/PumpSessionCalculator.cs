using FuelStationERP.Domain.Entities;

namespace FuelStationERP.Domain.Services;

/// <summary>Applies the meter, RTT, sales and cash reconciliation rules observed in Version 3.</summary>
public static class PumpSessionCalculator
{
    public static void Calculate(PumpSession session)
    {
        if (session.OpeningMeter < 0 || session.ClosingMeter < 0 || session.PricePerLitre < 0 ||
            session.CashCollected < 0 || session.PosAmount < 0 || session.Expenses < 0 || session.RttLitres < 0)
            throw new ArgumentOutOfRangeException(nameof(session), "Session meter and amount values cannot be negative.");
        if (session.ClosingMeter < session.OpeningMeter)
            throw new ArgumentException("Closing meter cannot be less than opening meter.", nameof(session));

        var grossDispensed = session.ClosingMeter - session.OpeningMeter;
        if (session.RttLitres > grossDispensed)
            throw new ArgumentException("RTT litres cannot exceed the pump meter movement.", nameof(session));

        session.LitresSold = grossDispensed - session.RttLitres;
        session.SaleAmount = session.LitresSold * session.PricePerLitre;
        session.ShortOver = session.CashCollected + session.PosAmount - (session.SaleAmount - session.Expenses);
    }
}
