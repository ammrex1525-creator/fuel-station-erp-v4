using Android.App;
using Android.Runtime;
using Microsoft.Maui;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace FuelStationERP.Mobile;

[Application]
public sealed class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
