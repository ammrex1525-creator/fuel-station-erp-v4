using Microsoft.Maui;

namespace FuelStationERP.Mobile.WinUI;

public sealed partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
