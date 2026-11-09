# FuelStationERP Version 4

Version 4 is an API-first .NET ERP with web, Windows desktop and mobile clients. The active solution contains `FuelStationERP.API`, `FuelStationERP.Web`, `FuelStationERP.Desktop`, `FuelStationERP.Mobile`, shared domain rules and persistence. Web and mobile operations use the API so validation and station calculations live in one backend.

Version 3 at `C:\Users\ammar\FuelStationERP_V2\Version3` was inspected as a reference and was not modified. The V4 app starts with a fresh local database; it does not copy or import Version 3 transactions.

## Open and run

1. Open `FuelStationERP.sln` in Visual Studio Community.
2. Set `FuelStationERP.API` as the startup project to run the backend. Run Web and Desktop separately. Mobile targets Android, iOS, Mac Catalyst and Windows; install the matching MAUI workload and platform SDKs to build each target.
3. Press **F5**.

Run the API from this folder:

```powershell
dotnet run --project src/FuelStationERP.API/FuelStationERP.API.csproj
```

Before first API startup, configure process environment secrets. Use a unique JWT signing key of at least 32 bytes, plus the first owner login. The owner is created only when the Identity database has no users:

```powershell
$env:Jwt__SigningKey = "replace-with-a-unique-secret-of-at-least-32-characters"
$env:Bootstrap__Username = "station-owner"
$env:Bootstrap__Email = "owner@example.com"
$env:Bootstrap__Password = "replace-with-a-strong-password"
dotnet run --project src/FuelStationERP.API/FuelStationERP.API.csproj
```

Sign in at `POST /api/v1/auth/login` and send the returned bearer token to protected endpoints. The API uses SQLite at `%LOCALAPPDATA%\FuelStationERP\station-v4.db` by default; `Database__Path` can override it. It seeds a Main Branch and PMS, AGO and LPG products when those tables are empty.

For one shared database across API clients, configure `ConnectionStrings__Erp` with a SQL Server connection string before starting the API. Keep the connection secret outside source control and deploy the API behind HTTPS before connecting phones or remote browsers.

The WPF desktop can use the same `ConnectionStrings__Erp` environment variable to connect to the shared SQL Server database. Set it in the desktop process environment too. Without it, the desktop continues to use its local SQLite file.

Run the web portal in a second terminal after the API is running:

```powershell
dotnet run --project src/FuelStationERP.Web/FuelStationERP.Web.csproj
```

Open `https://localhost:7291`. The responsive portal provides a station overview, daily branch performance cards, recent pump sessions, persistent branch operations navigation, fuel and equipment setup with selectable products and tanks, receipts, expenses, pump sessions, tank readings, owner user management, and a date-filtered Report Center with PDF, Excel (.xlsx) and CSV downloads. Set `Api__BaseUrl` when the API is hosted elsewhere.

Run the desktop app from this folder:

```powershell
dotnet run --project src/FuelStationERP.Desktop/FuelStationERP.Desktop.csproj
```

Run the phone app from Visual Studio by selecting `FuelStationERP.Mobile` and an Android emulator or connected device. Enter the API’s reachable HTTPS address and sign in; choose an assigned branch from the station list. Do not use `localhost` on a phone; use the server host name or LAN address. Managers can create tanks, pumps and full attendant records, then record opening deliveries. The mobile Report Center filters dates and shares PDF, Excel (.xlsx) or CSV files with the device’s available apps. For command-line Android builds, install the Google Android SDK and set its path for MSBuild if it is not detected automatically. iOS device builds require a paired Mac with Apple’s build tools.

The database is created in `%LOCALAPPDATA%\FuelStationERP\station-v4.db`. The first launch creates a Main Branch, Cash Till and POS Clearing accounts, plus the PMS, AGO and LPG products. It does not create tanks, pumps, staff, stock receipts or example financial transactions; set those up for the actual station before posting.

Build the active solution with:

```powershell
dotnet build FuelStationERP.sln
```

## Included station workflows

- Overview of today's pump sales, LPG sales, expenses and short/over, plus recent activity.
- Branch creation and branch switching; operational records are branch-scoped.
- Product, tank, pump and attendant setup.
- Fuel deliveries with invoice and received quantities, supplier/invoice/delivery details, vehicle and driver details, capacity checks and FIFO cost batches.
- Pump sessions with carried-forward opening meter, closing meter, price, cash/POS, session expense, RTT and short/over calculation. Posting allocates stock FIFO and records COGS and gross profit.
- LPG fill/sale by kilograms with a per-kg selling price, cash/POS, optional cylinder count and size, stock limit, FIFO COGS and gross profit. LPG does not use pump-meter sessions.
- Physical tank stock checks with book-versus-physical variance.
- Operating expenses, cash/POS collections linked to pump sessions, account setup and account transactions.
- Sales report including pump sessions, LPG fills and expenses.

Version 3 semantics retained for metered pump sessions: litres sold = closing meter − opening meter − RTT; sales = quantity sold × unit price; short/over = cash + POS − (sales − session expense); FIFO COGS consumes the oldest eligible same-product delivery batches. LPG uses a separate weighed-fill sale because a cylinder fill is not represented by a pump meter.

## LPG handling

LPG is seeded as a non-metered product with **kg** as its unit. Record LPG deliveries and tank capacity in kg, then record each fill on the **LPG Fills** tab. Cylinder count and cylinder size are optional descriptors for each fill; this version does not maintain reusable cylinder assets, deposits or returns. LPG tank readings are recorded as physical stock quantities in kg; enter the station's verified measurement result rather than converting a dip stick reading without its calibration chart.

PMS and AGO are seeded as metered products in litres. Add tanks, pumps and attendants under **Station Setup**, record a costed opening delivery for each tank, then use **Pump Sessions**. A pump session cannot post without enough stock in eligible delivery batches.

## Data and architecture

- `FuelStationERP.API`: authenticated backend endpoints for identity, branches, products, station setup and operations. Route groups include users, tanks, pumps, pump runs, attendants, suppliers, fuel receipts, expenses, tank dips, pump sessions, LPG fills and offline submissions.
- `FuelStationERP.Web`: responsive browser portal backed by the authenticated API.
- `FuelStationERP.Mobile`: .NET MAUI phone client for manager dashboard, pump runs and sessions, LPG fills, tank readings and expense capture.
- `FuelStationERP.Desktop`: WPF operator interface. It can use a local SQLite file or share the SQL Server database when configured.
- `FuelStationERP.Domain`: station entities and Version 3 pump-session calculations.
- `FuelStationERP.Infrastructure`: EF Core persistence, Identity-ready user entities and FIFO services.
- SQL Server is the shared deployment option for API and desktop data. SQLite remains available for local development. Back up the selected database independently.
- Branch IDs are recorded throughout operational entities. The API enforces branch claims on branch-scoped endpoints.

The web and mobile clients use the API. The existing WPF screens share SQL Server data when configured but still access the database directly. PDF, Excel and CSV report exports are available across desktop, web and mobile. Payroll processing, supplier settlement, edit/reversal workflows, delivery shortage settlement, attendant short recovery allocation and Version 3 data import still need separate business-policy and migration work before production rollout.

## Migration reference

See [MIGRATION.md](MIGRATION.md) for the Version 3 source map and financial rules. User passwords from Version 3 are never copied. The station operator must enter real master data and approved opening stock in Version 4; do not treat the pre-existing Version 3 balances as imported.
