namespace FuelStationERP.Mobile;

public partial class App : Application
{
    public App() => InitializeComponent();
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(new MainPage()) { BarBackgroundColor = Color.FromArgb("#14201D"), BarTextColor = Color.FromArgb("#EAF0EF") });
}
