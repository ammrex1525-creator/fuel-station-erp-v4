using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace FuelStationERP.Mobile;

public sealed class MainPage : ContentPage
{
    readonly Entry _api = new() { Placeholder = "API address, e.g. https://station.example.com", Keyboard = Keyboard.Url };
    readonly Entry _username = new() { Placeholder = "Username" };
    readonly Entry _password = new() { Placeholder = "Password", IsPassword = true };
    readonly Picker _branch = new() { Title = "Choose your station" };
    readonly Label _status = new() { TextColor = Color.FromArgb("#98AAA4"), FontSize = 13 };
    readonly Label _summary = new() { Text = "Sign in to view station activity.", TextColor = Color.FromArgb("#EAF0EF"), FontSize = 18 };
    readonly Label _reportSummary = new() { Text = "Choose a station and refresh the report.", TextColor = Color.FromArgb("#EAF0EF"), FontSize = 14 };
    readonly Label _reportRows = new() { TextColor = Color.FromArgb("#98AAA4"), FontSize = 12 };
    readonly VerticalStackLayout _reportCharts = new() { Spacing = 8 };
    readonly DatePicker _reportFrom = new() { Date = DateTime.Today.AddDays(-29), MaximumDate = DateTime.Today };
    readonly DatePicker _reportTo = new() { Date = DateTime.Today, MaximumDate = DateTime.Today };
    readonly Picker _reportCategory = new() { Title = "Report category" };
    readonly Entry _expenseDescription = new() { Placeholder = "Expense description" };
    readonly Entry _expenseAmount = new() { Placeholder = "Amount", Keyboard = Keyboard.Numeric };
    readonly Entry _expenseCategory = new() { Placeholder = "Category" };
    readonly Picker _expenseAccountPicker = new() { Title = "Paid from account (optional)" };
    readonly Picker _pump = new() { Title = "Select pump" };
    readonly Picker _attendant = new() { Title = "Select attendant" };
    readonly Picker _run = new() { Title = "Select open pump run" };
    readonly Entry _runOpening = Numeric("Opening meter for a new run");
    readonly Entry _openMeter = Numeric("Opening meter");
    readonly Entry _closeMeter = Numeric("Closing meter");
    readonly Entry _price = Numeric("Price per litre");
    readonly Entry _cash = Numeric("Cash collected");
    readonly Entry _pos = Numeric("POS collected");
    readonly Entry _sessionExpense = Numeric("Session expense");
    readonly Entry _rtt = Numeric("RTT litres");
    readonly Picker _tank = new() { Title = "Select tank" };
    readonly Picker _lpgTank = new() { Title = "Select LPG tank" };
    readonly Picker _product = new() { Title = "Product for new tank" };
    readonly Picker _setupTank = new() { Title = "Tank for new pump" };
    readonly Picker _receiptTank = new() { Title = "Tank receiving delivery" };
    readonly Entry _tankNumber = new() { Placeholder = "Tank number" };
    readonly Entry _tankCapacity = Numeric("Working capacity (L or kg)");
    readonly Entry _pumpNumber = new() { Placeholder = "Pump number" };
    readonly Entry _invoiceNumber = new() { Placeholder = "Invoice number (optional)" };
    readonly Entry _receivedQuantity = Numeric("Received quantity (L or kg)");
    readonly Entry _unitCost = Numeric("Cost per L or kg");
    readonly Picker _shortReceipt = new() { Title = "Delivery with an invoice shortage" };
    readonly Picker _shortPartyType = new() { Title = "Responsible supplier or driver" };
    readonly Entry _shortPartyName = new() { Placeholder = "Supplier / driver name" };
    readonly Entry _shortSettledLitres = Numeric("Settled shortage quantity");
    readonly Entry _shortSettledCost = Numeric("Settled shortage cost (NGN)");
    readonly Entry _shortStationLitres = Numeric("Station-borne shortage quantity");
    readonly Entry _shortStationCost = Numeric("Station-borne shortage cost (NGN)");
    readonly CheckBox _shortDisputed = new();
    readonly Entry _attendantName = new() { Placeholder = "Attendant full name" };
    readonly Entry _attendantPhone = new() { Placeholder = "Phone number", Keyboard = Keyboard.Telephone };
    readonly DatePicker _attendantDateJoined = new() { Date = DateTime.Today, MaximumDate = DateTime.Today };
    readonly Entry _attendantSalary = Numeric("Monthly salary (NGN)");
    readonly Entry _attendantPayday = Numeric("Salary payday (1–31, optional)");
    readonly Entry _dip = Numeric("Physical quantity (L or kg)");
    readonly Entry _dipOpening = Numeric("Opening stock baseline (L or kg)");
    readonly Entry _saleQuantity = Numeric("Fill quantity (kg)");
    readonly Entry _salePrice = Numeric("Price per kg");
    readonly Entry _saleCash = Numeric("Cash collected");
    readonly Entry _salePos = Numeric("POS collected");
    readonly Entry _cylinderCount = Numeric("Cylinder count (optional)");
    readonly Picker _mobileAccountType = new() { Title = "Account type" };
    readonly Entry _mobileAccountName = new() { Placeholder = "Account name" };
    readonly Entry _mobileAccountNumber = new() { Placeholder = "Account number (optional)" };
    readonly Entry _mobileAccountProvider = new() { Placeholder = "Bank / POS provider (optional)" };
    readonly Entry _mobileOpeningBalance = Numeric("Opening balance (NGN)");
    readonly Picker _mobileTransactionType = new() { Title = "Account transaction" };
    readonly Picker _mobileAccount = new() { Title = "From / account" };
    readonly Picker _mobileTargetAccount = new() { Title = "To account (transfer)" };
    readonly Picker _mobileReceipt = new() { Title = "Delivery (supplier payment)" };
    readonly Entry _mobileTransactionAmount = Numeric("Amount (NGN)");
    readonly Entry _mobileTransactionReference = new() { Placeholder = "Reference / supplier" };
    readonly DatePicker _reconciliationDate = new() { Date = DateTime.Today, MaximumDate = DateTime.Today };
    readonly Entry _vaultCountAmount = Numeric("Physical cash vault count (NGN)");
    readonly Label _mobileAccountSummary = new() { TextColor = Color.FromArgb("#EAF0EF"), FontSize = 13 };
    readonly Label _mobileReconciliationSummary = new() { TextColor = Color.FromArgb("#98AAA4"), FontSize = 13 };
    HttpClient? _client;
    readonly Border _setupCard;
    readonly Border _deliveryCard;
    readonly Border _accountsCard;
    string[] _roles = [];
    BranchRole[] _branchRoles = [];

    public MainPage()
    {
        Title = "FuelStation ERP";
        BackgroundColor = Color.FromArgb("#101A19");
        _api.Text = Preferences.Default.Get("erp_api", "");
        var login = new Button { Text = "Sign in", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        login.Clicked += async (_, _) => await LoginAsync();
        var refresh = new Button { Text = "Refresh dashboard", BackgroundColor = Color.FromArgb("#263B36"), TextColor = Colors.White };
        refresh.Clicked += async (_, _) => await LoadDashboardAsync();
        var saveExpense = new Button { Text = "Save expense", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        saveExpense.Clicked += async (_, _) => await SaveExpenseAsync();
        var saveDip = new Button { Text = "Save tank reading", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        saveDip.Clicked += async (_, _) => await SaveDipAsync();
        var saveSale = new Button { Text = "Record LPG fill", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        saveSale.Clicked += async (_, _) => await SaveProductSaleAsync();
        var saveSession = new Button { Text = "Post pump session", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        saveSession.Clicked += async (_, _) => await SaveSessionAsync();
        var openRun = new Button { Text = "Open pump run", BackgroundColor = Color.FromArgb("#263B36"), TextColor = Colors.White };
        openRun.Clicked += async (_, _) => await OpenRunAsync();
        var createTank = new Button { Text = "Create tank", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        createTank.Clicked += async (_, _) => await CreateTankAsync();
        var createPump = new Button { Text = "Create pump", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        createPump.Clicked += async (_, _) => await CreatePumpAsync();
        var recordDelivery = new Button { Text = "Record opening delivery", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        recordDelivery.Clicked += async (_, _) => await RecordDeliveryAsync();
        var createAttendant = new Button { Text = "Create attendant", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        createAttendant.Clicked += async (_, _) => await CreateAttendantAsync();
        _mobileAccountType.ItemsSource = new[] { "CASH", "BANK", "POS" }; _mobileAccountType.SelectedIndex = 0;
        _mobileTransactionType.ItemsSource = new[] { "Opening Balance", "Account Receipt", "Cash Count", "Cash Expense", "POS Transaction", "Account Expense", "Product Payment", "Account Payment / Amount Sent", "Bank Deposit", "POS Settlement", "Account Transfer" }; _mobileTransactionType.SelectedIndex = 0;
        var createAccount = new Button { Text = "Create account", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") }; createAccount.Clicked += async (_, _) => await CreateAccountAsync();
        var saveAccountTransaction = new Button { Text = "Post account transaction", BackgroundColor = Color.FromArgb("#263B36"), TextColor = Colors.White }; saveAccountTransaction.Clicked += async (_, _) => await SaveAccountTransactionAsync();
        var refreshReconciliation = new Button { Text = "Refresh daily reconciliation", BackgroundColor = Color.FromArgb("#263B36"), TextColor = Colors.White }; refreshReconciliation.Clicked += async (_, _) => await RefreshMobileReconciliationAsync();
        var saveVaultCount = new Button { Text = "Record vault count", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") }; saveVaultCount.Clicked += async (_, _) => await RecordMobileVaultCountAsync();
        _accountsCard = Card("Accounts · Cash Vault · POS reconciliation", new Label { Text = "Named bank/POS accounts, linked delivery payments and daily cash close.", TextColor = Color.FromArgb("#98AAA4"), FontSize = 13 }, _mobileAccountSummary, _mobileAccountName, _mobileAccountType, _mobileAccountNumber, _mobileAccountProvider, _mobileOpeningBalance, createAccount, _mobileTransactionType, _mobileAccount, _mobileTargetAccount, _mobileReceipt, _mobileTransactionAmount, _mobileTransactionReference, saveAccountTransaction, _reconciliationDate, refreshReconciliation, _mobileReconciliationSummary, _vaultCountAmount, saveVaultCount);
        var refreshReport = new Button { Text = "Refresh report", BackgroundColor = Color.FromArgb("#263B36"), TextColor = Colors.White };
        refreshReport.Clicked += async (_, _) => await LoadReportAsync();
        _reportCategory.ItemsSource = new[] { "Summary", "Product / Delivery", "Profit & Loss", "Sales", "Tank Stock", "Fuel Receipts", "Attendants", "Expenses", "RTT", "Documents" }; _reportCategory.SelectedIndex = 0;
        _reportCategory.SelectedIndexChanged += async (_, _) => await LoadReportAsync();
        var shareCsv = new Button { Text = "Share CSV", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        shareCsv.Clicked += async (_, _) => await ShareReportAsync("csv");
        var shareExcel = new Button { Text = "Share Excel workbook", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        shareExcel.Clicked += async (_, _) => await ShareReportAsync("xlsx");
        var sharePdf = new Button { Text = "Share PDF", BackgroundColor = Color.FromArgb("#E8B957"), TextColor = Color.FromArgb("#14201D") };
        sharePdf.Clicked += async (_, _) => await ShareReportAsync("pdf");
        _setupCard = Card("Station setup · managers", new Label { Text = "Create a product-specific tank, assign each pump to a metered tank, and add the attendants used on pump sessions.", TextColor = Color.FromArgb("#98AAA4"), FontSize = 13 }, _product, _tankNumber, _tankCapacity, createTank, _setupTank, _pumpNumber, createPump, _attendantName, _attendantPhone, _attendantDateJoined, _attendantSalary, _attendantPayday, createAttendant);
        _shortPartyType.ItemsSource = new[] { "Supplier", "Driver" }; _shortPartyType.SelectedIndex = 0;
        _price.IsReadOnly = true; _salePrice.IsReadOnly = true;
        _deliveryCard = Card("Deliveries and shortages · managers", new Label { Text = "Post received quantities and record any invoice shortage settlement or station-borne amount.", TextColor = Color.FromArgb("#98AAA4"), FontSize = 13 }, _receiptTank, _invoiceNumber, _receivedQuantity, _unitCost, recordDelivery, _shortReceipt, _shortPartyType, _shortPartyName, _shortSettledLitres, _shortSettledCost, _shortStationLitres, _shortStationCost, new HorizontalStackLayout { Spacing = 8, Children = { _shortDisputed, new Label { Text = "Disputed", TextColor = Color.FromArgb("#EAF0EF"), VerticalTextAlignment = TextAlignment.Center } } }, new Button { Text = "Save delivery shortage reconciliation", Command = new Command(async () => await ReconcileReceiptShortAsync()), BackgroundColor = Color.FromArgb("#263B36"), TextColor = Colors.White });
        _setupCard.IsVisible = false; _deliveryCard.IsVisible = false;
        _accountsCard.IsVisible = false;

        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(22, 22), Spacing = 14,
            Children =
            {
                new Image { Source = "cleon_logo_dark.png", HeightRequest = 78, HorizontalOptions = LayoutOptions.Start, Aspect = Aspect.AspectFit },
                new Label { Text = "STATION OPERATIONS", TextColor = Color.FromArgb("#E8B957"), FontSize = 12, CharacterSpacing = 2 },
                new Label { Text = "Your station,\nin your pocket.", TextColor = Colors.White, FontSize = 32, FontAttributes = FontAttributes.Bold },
                Card("Connect · choose your station", _api, _username, _password, _branch, login),
                _status,
                Card("Today's overview", _summary, refresh),
                Card("Pump session", _pump, _attendant, _run, _runOpening, openRun, _openMeter, _closeMeter, _price, _cash, _pos, _sessionExpense, _rtt, saveSession),
                Card("Tank stock check", _tank, _dipOpening, _dip, saveDip),
                _setupCard,
                _deliveryCard,
                Card("LPG fill", _lpgTank, _saleQuantity, _salePrice, _saleCash, _salePos, _cylinderCount, saveSale),
                Card("Capture an expense", _expenseDescription, _expenseCategory, _expenseAmount, _expenseAccountPicker, saveExpense),
                _accountsCard,
                Card("Report Center", new Label { Text = "Date range · choose a category; exports follow the selected report", TextColor = Color.FromArgb("#98AAA4"), FontSize = 12 }, _reportFrom, _reportTo, _reportCategory, refreshReport, _reportSummary, _reportCharts, _reportRows, sharePdf, shareExcel, shareCsv),
                new Label { Text = "Connected clients use your station API. Add the API’s HTTPS address above; phones should use the server’s reachable host name, not localhost.", TextColor = Color.FromArgb("#98AAA4"), FontSize = 12 }
            }
        }};
        _branch.SelectedIndexChanged += async (_, _) => await SwitchBranchAsync();
        _tank.SelectedIndexChanged += async (_, _) => await LoadDipOpeningAsync();
        _run.SelectedIndexChanged += (_, _) => { if (_run.SelectedItem is Choice { OpeningMeter: decimal opening }) _openMeter.Text = opening.ToString("0.##", CultureInfo.InvariantCulture); };
        _pump.SelectedIndexChanged += async (_, _) => await LoadEffectivePumpPriceAsync();
        _lpgTank.SelectedIndexChanged += async (_, _) => await LoadEffectiveLpgPriceAsync();
    }

    async Task LoginAsync()
    {
        try
        {
            var baseUrl = _api.Text?.Trim().TrimEnd('/') ?? "";
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { SetStatus("Enter a valid HTTPS API address.", true); return; }
            Preferences.Default.Set("erp_api", baseUrl);
            var client = new HttpClient { BaseAddress = new Uri(baseUrl + "/") };
            var response = await client.PostAsJsonAsync("api/v1/auth/login", new { username = _username.Text, password = _password.Text });
            if (!response.IsSuccessStatusCode) { SetStatus("Sign-in failed. Check your username and password.", true); return; }
            var result = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (string.IsNullOrWhiteSpace(result?.AccessToken)) { SetStatus("The API did not return a sign-in token.", true); return; }
            _roles = result.Roles ?? []; _branchRoles = result.Branches ?? [];
            await SecureStorage.Default.SetAsync("erp_token", result.AccessToken);
            _client = client; _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
            _password.Text = "";
            SetStatus("Connected securely. Choose a station to continue."); await LoadBranchesAsync();
        }
        catch (Exception ex) { SetStatus($"Could not connect: {ex.Message}", true); }
    }

    async Task EnsureClientAsync()
    {
        if (_client is not null) return;
        var baseUrl = Preferences.Default.Get("erp_api", ""); var token = await SecureStorage.Default.GetAsync("erp_token");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Sign in first.");
        _client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var saved = await SecureStorage.Default.GetAsync("erp_branch") ?? "";
        if (_branch.SelectedItem is null && _branch.ItemsSource is IEnumerable<Choice> branches)
            _branch.SelectedItem = branches.FirstOrDefault(x => x.Id.ToString(CultureInfo.InvariantCulture) == saved);
    }

    async Task LoadChoicesAsync()
    {
        await EnsureClientAsync(); var root = $"api/v1/branches/{BranchId}"; var client = _client ?? throw new InvalidOperationException("Sign in first.");
        _pump.ItemsSource = await ReadChoicesAsync($"{root}/pumps", "number");
        _attendant.ItemsSource = await ReadChoicesAsync($"{root}/attendants", "name");
        _run.ItemsSource = await ReadChoicesAsync($"{root}/pump-runs?status=OPEN", "id", "pumpId");
        var receipts = await client.GetFromJsonAsync<List<JsonElement>>($"{root}/fuel-receipts") ?? [];
        _shortReceipt.ItemsSource = receipts.Where(x => x.GetProperty("invoiceLitres").GetDecimal() > x.GetProperty("receivedLitres").GetDecimal())
            .Select(x => new Choice(x.GetProperty("id").GetInt64(), $"Receipt #{x.GetProperty("id")} · short {(x.GetProperty("invoiceLitres").GetDecimal() - x.GetProperty("receivedLitres").GetDecimal()):N2} {x.GetProperty("unitOfMeasureSnapshot").GetString()}" )).ToList();
        _mobileReceipt.ItemsSource = receipts.Select(x => new Choice(x.GetProperty("id").GetInt64(), $"Receipt #{x.GetProperty("id")} · {x.GetProperty("productCodeSnapshot")} · {x.GetProperty("supplierNameSnapshot").GetString()} · ₦{x.GetProperty("totalCost").GetDecimal():N2}")).ToList();
        var accountRows = await client.GetFromJsonAsync<List<JsonElement>>($"{root}/accounts") ?? [];
        _mobileAccount.ItemsSource = accountRows.Select(x => new Choice(x.GetProperty("id").GetInt64(), $"{x.GetProperty("name")} · {x.GetProperty("accountType")} · ₦{x.GetProperty("balance").GetDecimal():N2}")).ToList();
        _expenseAccountPicker.ItemsSource = new[] { new Choice(0, "Unassigned") }.Concat(accountRows.Select(x => new Choice(x.GetProperty("id").GetInt64(), $"{x.GetProperty("name")} · {x.GetProperty("accountType")}"))).ToList();
        _mobileTargetAccount.ItemsSource = new[] { new Choice(0, "No destination") }.Concat(accountRows.Select(x => new Choice(x.GetProperty("id").GetInt64(), $"{x.GetProperty("name")} · {x.GetProperty("accountType")}"))).ToList();
        _mobileAccountSummary.Text = string.Join("\n", accountRows.Select(x => $"{x.GetProperty("name")} ({x.GetProperty("accountType")}) · ₦{x.GetProperty("balance").GetDecimal():N2}"));
        var products = await client.GetFromJsonAsync<List<JsonElement>>("api/v1/products") ?? [];
        var names = products.ToDictionary(x => x.GetProperty("id").GetInt64(), x => x.GetProperty("code").ToString());
        var metered = products.Where(x => x.GetProperty("isMetered").GetBoolean()).Select(x => x.GetProperty("id").GetInt64()).ToHashSet();
        _product.ItemsSource = products.Select(x => new Choice(x.GetProperty("id").GetInt64(), $"{x.GetProperty("code")} · {x.GetProperty("name")} ({x.GetProperty("unitOfMeasure")})")).ToList();
        var tanks = await client.GetFromJsonAsync<List<JsonElement>>($"{root}/tanks") ?? [];
        var tankChoices = tanks.Select(t => { var productId = t.GetProperty("productId").GetInt64(); var code = names.GetValueOrDefault(productId, "Product"); return (Tank: t, Choice: new Choice(t.GetProperty("id").GetInt64(), $"Tank {t.GetProperty("number")} · {code}")); }).ToList();
        _tank.ItemsSource = tankChoices.Select(x => x.Choice).ToList(); _setupTank.ItemsSource = tankChoices.Where(x => metered.Contains(x.Tank.GetProperty("productId").GetInt64())).Select(x => x.Choice).ToList(); _receiptTank.ItemsSource = tankChoices.Select(x => x.Choice).ToList();
        var nonMetered = products.Where(x => !x.GetProperty("isMetered").GetBoolean()).Select(x => x.GetProperty("id").GetInt64()).ToHashSet();
        _lpgTank.ItemsSource = tankChoices.Where(x => nonMetered.Contains(x.Tank.GetProperty("productId").GetInt64())).Select(x => x.Choice).ToList();
        await LoadEffectivePumpPriceAsync(); await LoadEffectiveLpgPriceAsync();
    }

    async Task LoadEffectivePumpPriceAsync()
    {
        try
        {
            if (_pump.SelectedItem is not Choice pump) return;
            await EnsureClientAsync(); var root = $"api/v1/branches/{BranchId}"; var client = _client ?? throw new InvalidOperationException("Sign in first.");
            var pumps = await client.GetFromJsonAsync<List<JsonElement>>($"{root}/pumps") ?? [];
            var pumpRow = pumps.FirstOrDefault(x => x.GetProperty("id").GetInt64() == pump.Id);
            if (pumpRow.ValueKind != JsonValueKind.Object) return;
            var tankId = pumpRow.GetProperty("tankId").GetInt64();
            var tanks = await client.GetFromJsonAsync<List<JsonElement>>($"{root}/tanks") ?? [];
            var tank = tanks.FirstOrDefault(x => x.GetProperty("id").GetInt64() == tankId);
            if (tank.ValueKind != JsonValueKind.Object) return;
            var productId = tank.GetProperty("productId").GetInt64();
            var price = await client.GetFromJsonAsync<JsonElement>($"{root}/product-prices/effective?productId={productId}&onDate={DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}");
            _price.Text = price.GetProperty("unitPrice").GetDecimal().ToString("0.00", CultureInfo.InvariantCulture);
        }
        catch { _price.Text = ""; }
    }

    async Task LoadEffectiveLpgPriceAsync()
    {
        try
        {
            if (_lpgTank.SelectedItem is not Choice tankChoice) return;
            await EnsureClientAsync(); var client = _client ?? throw new InvalidOperationException("Sign in first."); var tanks = await client.GetFromJsonAsync<List<JsonElement>>($"api/v1/branches/{BranchId}/tanks") ?? [];
            var tank = tanks.FirstOrDefault(x => x.GetProperty("id").GetInt64() == tankChoice.Id);
            if (tank.ValueKind != JsonValueKind.Object) return;
            var productId = tank.GetProperty("productId").GetInt64();
            var price = await client.GetFromJsonAsync<JsonElement>($"api/v1/branches/{BranchId}/product-prices/effective?productId={productId}&onDate={DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}");
            _salePrice.Text = price.GetProperty("unitPrice").GetDecimal().ToString("0.00", CultureInfo.InvariantCulture);
        }
        catch { _salePrice.Text = ""; }
    }

    async Task LoadBranchesAsync()
    {
        await EnsureClientAsync();
        var branches = await _client!.GetFromJsonAsync<List<JsonElement>>("api/v1/branches") ?? [];
        _branch.ItemsSource = branches.Select(x => new Choice(x.GetProperty("id").GetInt64(), $"{x.GetProperty("name")} · {x.GetProperty("code")}")).ToList();
        var saved = await SecureStorage.Default.GetAsync("erp_branch") ?? "";
        _branch.SelectedItem = ((IEnumerable<Choice>)_branch.ItemsSource).FirstOrDefault(x => x.Id.ToString(CultureInfo.InvariantCulture) == saved)
            ?? ((IEnumerable<Choice>)_branch.ItemsSource).FirstOrDefault();
        if (_branch.SelectedItem is null) SetStatus("Your account has no active station access. Ask an owner to assign a branch.", true);
        else await SwitchBranchAsync();
    }

    async Task SwitchBranchAsync()
    {
        if (_branch.SelectedItem is not Choice branch || _client is null) return;
        var canManage = _roles.Contains("OWNER", StringComparer.OrdinalIgnoreCase) || _branchRoles.Any(x => x.BranchId == branch.Id && (x.Role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) || x.Role.Equals("MANAGER", StringComparison.OrdinalIgnoreCase)));
        _setupCard.IsVisible = canManage; _deliveryCard.IsVisible = canManage;
        _accountsCard.IsVisible = canManage;
        await SecureStorage.Default.SetAsync("erp_branch", branch.Id.ToString(CultureInfo.InvariantCulture));
        try { await LoadChoicesAsync(); await LoadDashboardAsync(); await LoadReportAsync(); }
        catch (Exception ex) { SetStatus($"Could not load this station: {ex.Message}", true); }
    }

    async Task<List<Choice>> ReadChoicesAsync(string path, string label, string? extra = null)
    {
        var rows = await _client!.GetFromJsonAsync<List<JsonElement>>(path) ?? [];
        return rows.Select(row => new Choice(row.GetProperty("id").GetInt64(), extra is null ? row.GetProperty(label).ToString() : $"Run {row.GetProperty("id")} · pump {row.GetProperty(extra)}", row.TryGetProperty("openingMeter", out var meter) && meter.ValueKind == JsonValueKind.Number ? meter.GetDecimal() : null)).ToList();
    }

    async Task LoadDashboardAsync()
    {
        try
        {
            await EnsureClientAsync(); var data = await _client!.GetFromJsonAsync<JsonElement>($"api/v1/branches/{BranchId}/dashboard");
            _summary.Text = $"Fuel sales  {Money(data.GetProperty("fuelSales"))}\nOther product sales  {Money(data.GetProperty("otherProductSales"))}\nGross profit  {Money(data.GetProperty("grossProfit"))}\nExpenses  {Money(data.GetProperty("expenses"))}\nNet operating profit  {Money(data.GetProperty("netOperatingProfit"))}\nShort / over  {Money(data.GetProperty("shortOver"))}";
            SetStatus("Dashboard refreshed.");
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task<JsonElement> FetchReportAsync()
    {
        var fromDate = _reportFrom.Date ?? DateTime.Today; var toDate = _reportTo.Date ?? DateTime.Today;
        if (toDate < fromDate) throw new InvalidOperationException("The end date must be on or after the start date.");
        if ((toDate - fromDate).TotalDays > 366) throw new InvalidOperationException("Choose a report period of 367 days or less.");
        await EnsureClientAsync();
        return await _client!.GetFromJsonAsync<JsonElement>($"api/v1/branches/{BranchId}/reports/operations?from={DateOnly.FromDateTime(fromDate):yyyy-MM-dd}&to={DateOnly.FromDateTime(toDate):yyyy-MM-dd}");
    }

    JsonElement ApplyReportCategory(JsonElement report)
    {
        var category = _reportCategory.SelectedItem?.ToString() ?? "Summary";
        var root = JsonNode.Parse(report.GetRawText())!.AsObject();
        var rows = (root["rows"] as JsonArray ?? []).Where(x => x is JsonObject row && IsInReportCategory(row["type"]?.ToString() ?? "", category)).Select(x => x!.DeepClone()).ToList();
        var salesRows = rows.OfType<JsonObject>().Where(x => x["type"]?.ToString() is "Pump session" or "Product sale" or "Attendant activity").ToList();
        decimal Value(JsonObject x, string key) => decimal.TryParse(x[key]?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0m;
        var sales = salesRows.Sum(x => Value(x, "sales")); var cogs = salesRows.Sum(x => Value(x, "costOfGoodsSold")); var gross = salesRows.Sum(x => Value(x, "grossProfit"));
        var expenses = -rows.OfType<JsonObject>().Where(x => x["type"]?.ToString() == "Expense").Sum(x => Value(x, "sales"));
        root["rows"] = new JsonArray(rows.ToArray());
        root["totals"] = JsonSerializer.SerializeToNode(new { sales, costOfGoodsSold = cogs, grossProfit = gross, expenses, netOperatingProfit = gross - expenses, shortOver = salesRows.Sum(x => Value(x, "shortOver")) });
        return JsonSerializer.Deserialize<JsonElement>(root.ToJsonString());
    }

    static bool IsInReportCategory(string type, string category) => category switch
    {
        "Summary" => type != "Attendant activity",
        "Product / Delivery" => type is "Fuel delivery" or "Pump session" or "Product sale",
        "Profit & Loss" => type is "Pump session" or "Product sale" or "Expense",
        "Sales" => type is "Pump session" or "Product sale",
        "Tank Stock" => type is "Opening baseline" or "Tank stock check",
        "Fuel Receipts" => type == "Fuel delivery",
        "Attendants" => type == "Attendant activity",
        "Expenses" => type == "Expense",
        "RTT" => type == "RTT returned",
        _ => false
    };

    async Task LoadReportAsync()
    {
        try
        {
            var report = ApplyReportCategory(await FetchReportAsync()); var totals = report.GetProperty("totals"); var rows = report.GetProperty("rows");
            _reportSummary.Text = $"Sales  {Money(totals.GetProperty("sales"))}   ·   Gross profit  {Money(totals.GetProperty("grossProfit"))}\nExpenses  {Money(totals.GetProperty("expenses"))}   ·   Net operating profit  {Money(totals.GetProperty("netOperatingProfit"))}\nShort / over  {Money(totals.GetProperty("shortOver"))}   ·   {rows.GetArrayLength()} records";
            _reportRows.Text = string.Join("\n", rows.EnumerateArray().Reverse().Take(20).Select(x => $"{x.GetProperty("date").GetString()}  ·  {x.GetProperty("type").GetString()}  ·  {x.GetProperty("product").GetString()}  ·  {Money(x.GetProperty("sales"))}"));
            await RenderReportChartsAsync(totals);
            SetStatus("Report refreshed.");
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task RenderReportChartsAsync(JsonElement totals)
    {
        _reportCharts.Children.Clear();
        _reportCharts.Children.Add(new Label { Text = "Period performance", TextColor = Color.FromArgb("#E8B957"), FontAttributes = FontAttributes.Bold });
        var metrics = new[]
        {
            ("Sales", totals.GetProperty("sales").GetDecimal()),
            ("Cost of goods sold", totals.GetProperty("costOfGoodsSold").GetDecimal()),
            ("Gross profit", totals.GetProperty("grossProfit").GetDecimal()),
            ("Expenses", totals.GetProperty("expenses").GetDecimal()),
            ("Net operating profit", totals.GetProperty("netOperatingProfit").GetDecimal())
        };
        var max = Math.Max(1m, metrics.Max(x => Math.Abs(x.Item2)));
        foreach (var (name, value) in metrics)
        {
            _reportCharts.Children.Add(new Label { Text = $"{name}  {value:C0}", TextColor = Colors.White, FontSize = 12 });
            var progress = new ProgressBar { Progress = 0, ProgressColor = Color.FromArgb("#E8B957"), BackgroundColor = Color.FromArgb("#263B36") };
            _reportCharts.Children.Add(progress);
            await progress.ProgressTo((double)(Math.Abs(value) / max), 180, Easing.CubicOut);
        }
        _reportCharts.Children.Add(new Label { Text = "Tank gauges · latest closing dip", TextColor = Color.FromArgb("#E8B957"), FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) });
        await EnsureClientAsync();
        var client = _client ?? throw new InvalidOperationException("Sign in first.");
        var tanks = await client.GetFromJsonAsync<List<JsonElement>>($"api/v1/branches/{BranchId}/tanks") ?? [];
        var dips = await client.GetFromJsonAsync<List<JsonElement>>($"api/v1/branches/{BranchId}/tank-dips") ?? [];
        foreach (var tank in tanks)
        {
            var id = tank.GetProperty("id").GetInt64(); var number = tank.GetProperty("number").ToString();
            var capacity = tank.GetProperty("capacityLitres").GetDecimal();
            var dip = dips.Where(x => x.GetProperty("tankId").GetInt64() == id).OrderByDescending(x => x.GetProperty("recordedAt").GetDateTimeOffset()).FirstOrDefault();
            var closing = dip.ValueKind == JsonValueKind.Object ? dip.GetProperty("closingDip").GetDecimal() : 0m;
            var amount = capacity <= 0 ? 0d : Math.Clamp((double)(closing / capacity), 0, 1);
            _reportCharts.Children.Add(new Label { Text = dip.ValueKind == JsonValueKind.Object ? $"Tank {number}  {closing:N1} / {capacity:N1}  ({amount:P0})" : $"Tank {number}  No closing dip recorded", TextColor = Colors.White, FontSize = 12 });
            var gauge = new ProgressBar { Progress = amount, ProgressColor = Color.FromArgb("#35B779"), BackgroundColor = Color.FromArgb("#263B36") };
            _reportCharts.Children.Add(gauge);
        }
        if (tanks.Count == 0) _reportCharts.Children.Add(new Label { Text = "No active tanks are set up yet.", TextColor = Color.FromArgb("#98AAA4"), FontSize = 12 });
    }

    async Task ShareReportAsync(string format)
    {
        try
        {
            var fromDate = _reportFrom.Date ?? DateTime.Today; var toDate = _reportTo.Date ?? DateTime.Today;
            var report = ApplyReportCategory(await FetchReportAsync()); var totals = report.GetProperty("totals"); var lines = new List<string>
            {
                "CLEON FuelStationERP Report Center", $"Branch,{BranchId}", $"Period,{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
                "Type,Date,Product / category,Quantity,Unit,Sales / expense (NGN),Cost of goods sold (NGN),Gross profit (NGN),Short / over (NGN)"
            };
            foreach (var row in report.GetProperty("rows").EnumerateArray()) lines.Add(string.Join(",", new[] { "type", "date", "product", "quantity", "unit", "sales", "costOfGoodsSold", "grossProfit", "shortOver" }.Select(k => CsvValue(row.GetProperty(k).ToString()))));
            lines.Add(string.Join(",", new[] { "TOTALS", "", "", "", "", totals.GetProperty("sales").ToString(), totals.GetProperty("costOfGoodsSold").ToString(), totals.GetProperty("grossProfit").ToString(), totals.GetProperty("shortOver").ToString() }.Select(CsvValue)));
            lines.Add($"Expenses,,,,,{totals.GetProperty("expenses")}"); lines.Add($"Net operating profit,,,,,{totals.GetProperty("netOperatingProfit")}");
            var baseName = $"CLEON-report-{DateTime.Now:yyyyMMdd-HHmmss}";
            var file = Path.Combine(FileSystem.CacheDirectory, baseName + "." + format);
            if (format == "csv") await File.WriteAllTextAsync(file, "\uFEFF" + string.Join("\r\n", lines), Encoding.UTF8);
            else if (format == "xlsx") await File.WriteAllBytesAsync(file, MakeExcelReport(report, fromDate, toDate));
            else await File.WriteAllBytesAsync(file, MakePdfReport(report, fromDate, toDate));
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Share CLEON station report", File = new ShareFile(file) });
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    static string CsvValue(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    static byte[] MakeExcelReport(JsonElement report, DateTime from, DateTime to)
    {
        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"; XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var headers = new[] { "Type", "Date", "Product / category", "Quantity", "Unit", "Sales / expense (NGN)", "Cost of goods sold (NGN)", "Gross profit (NGN)", "Short / over (NGN)" };
        var matrix = new List<object?[]> { new object?[] { "CLEON · FuelStationERP Report Center" }, new object?[] { $"Branch {report.GetProperty("branchId")} · {from:yyyy-MM-dd} to {to:yyyy-MM-dd}" }, Array.Empty<object?>(), headers.Cast<object?>().ToArray() };
        foreach (var row in report.GetProperty("rows").EnumerateArray()) matrix.Add(new object?[] { row.GetProperty("type").GetString(), row.GetProperty("date").GetString(), row.GetProperty("product").GetString(), row.GetProperty("quantity").GetDecimal(), row.GetProperty("unit").GetString(), row.GetProperty("sales").GetDecimal(), row.GetProperty("costOfGoodsSold").GetDecimal(), row.GetProperty("grossProfit").GetDecimal(), row.GetProperty("shortOver").GetDecimal() });
        var totals = report.GetProperty("totals"); matrix.Add(Array.Empty<object?>()); matrix.Add(new object?[] { "TOTALS", "", "", "", "", totals.GetProperty("sales").GetDecimal(), totals.GetProperty("costOfGoodsSold").GetDecimal(), totals.GetProperty("grossProfit").GetDecimal(), totals.GetProperty("shortOver").GetDecimal() });
        matrix.Add(new object?[] { "Expenses", "", "", "", "", totals.GetProperty("expenses").GetDecimal() }); matrix.Add(new object?[] { "Net operating profit", "", "", "", "", totals.GetProperty("netOperatingProfit").GetDecimal() });
        var sheet = new XElement(m + "sheetData");
        for (var r = 0; r < matrix.Count; r++)
        {
            var cells = matrix[r].Select((value, c) => value is decimal or int or long
                ? new XElement(m + "c", new XAttribute("r", CellRef(c + 1, r + 1)), new XElement(m + "v", Convert.ToString(value, CultureInfo.InvariantCulture)))
                : new XElement(m + "c", new XAttribute("r", CellRef(c + 1, r + 1)), new XAttribute("t", "inlineStr"), new XElement(m + "is", new XElement(m + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""))));
            sheet.Add(new XElement(m + "row", new XAttribute("r", r + 1), cells));
        }
        using var output = new MemoryStream(); using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            PutXml(zip, "[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            PutXml(zip, "_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            PutXml(zip, "xl/workbook.xml", new XDocument(new XElement(m + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(m + "sheets", new XElement(m + "sheet", new XAttribute("name", "Operations Report"), new XAttribute("sheetId", 1), new XAttribute(rel + "id", "rId1"))))));
            PutXml(zip, "xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            PutXml(zip, "xl/worksheets/sheet1.xml", new XDocument(new XElement(m + "worksheet", sheet)));
        }
        return output.ToArray();
    }

    static byte[] MakePdfReport(JsonElement report, DateTime from, DateTime to)
    {
        var totals = report.GetProperty("totals"); var lines = new List<string>
        {
            "CLEON  |  FUEL STATION ERP  |  REPORT CENTER", $"Branch {report.GetProperty("branchId")}  |  {from:yyyy-MM-dd} to {to:yyyy-MM-dd}",
            $"Sales NGN {totals.GetProperty("sales").GetDecimal():N2}    COGS NGN {totals.GetProperty("costOfGoodsSold").GetDecimal():N2}    Gross profit NGN {totals.GetProperty("grossProfit").GetDecimal():N2}",
            $"Expenses NGN {totals.GetProperty("expenses").GetDecimal():N2}    Net operating profit NGN {totals.GetProperty("netOperatingProfit").GetDecimal():N2}    Short / over NGN {totals.GetProperty("shortOver").GetDecimal():N2}",
            "TYPE        DATE        PRODUCT / CATEGORY       QTY   UNIT       SALES        COGS        GROSS PROFIT   SHORT/OVER"
        };
        foreach (var row in report.GetProperty("rows").EnumerateArray())
        {
            var product = row.GetProperty("product").GetString() ?? ""; if (product.Length > 18) product = product[..18];
            lines.Add($"{row.GetProperty("type").GetString(),-11} {row.GetProperty("date").GetString()}  {product,-18} {row.GetProperty("quantity").GetDecimal(),8:N2} {row.GetProperty("unit").GetString(),-4} {row.GetProperty("sales").GetDecimal(),10:N2} {row.GetProperty("costOfGoodsSold").GetDecimal(),10:N2} {row.GetProperty("grossProfit").GetDecimal(),12:N2} {row.GetProperty("shortOver").GetDecimal(),10:N2}");
        }
        const int perPage = 37; var pageCount = Math.Max(1, (int)Math.Ceiling(lines.Count / (double)perPage)); var objects = new List<byte[]> { Array.Empty<byte>() };
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>")); objects.Add(Encoding.ASCII.GetBytes("<< /Type /Pages /Count " + pageCount + " /Kids [" + string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{5 + 2 * i} 0 R")) + "] >>")); objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"));
        for (var p = 0; p < pageCount; p++)
        {
            var content = new StringBuilder("BT /F1 7 Tf 28 565 Td 9 TL\n"); foreach (var line in lines.Skip(p * perPage).Take(perPage)) content.Append('(').Append(PdfEscape(line)).AppendLine(") Tj T* "); content.Append("ET");
            var bytes = Encoding.ASCII.GetBytes(content.ToString()); var contentId = 6 + 2 * p;
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>"));
            objects.Add(Encoding.ASCII.GetBytes($"<< /Length {bytes.Length} >>\nstream\n" + content + "\nendstream"));
        }
        using var pdf = new MemoryStream(); pdf.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n")); var offsets = new long[objects.Count];
        for (var id = 1; id < objects.Count; id++) { offsets[id] = pdf.Position; pdf.Write(Encoding.ASCII.GetBytes($"{id} 0 obj\n")); pdf.Write(objects[id]); pdf.Write(Encoding.ASCII.GetBytes("\nendobj\n")); }
        var xref = pdf.Position; pdf.Write(Encoding.ASCII.GetBytes($"xref\n0 {objects.Count}\n0000000000 65535 f \n"));
        for (var id = 1; id < offsets.Length; id++) pdf.Write(Encoding.ASCII.GetBytes(offsets[id].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n"));
        pdf.Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {objects.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF")); return pdf.ToArray();
    }

    static void PutXml(ZipArchive zip, string path, string xml) { using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open(); var bytes = Encoding.UTF8.GetBytes(xml); stream.Write(bytes); }
    static void PutXml(ZipArchive zip, string path, XDocument doc) { using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open(); doc.Save(stream); }
    static string CellRef(int col, int row) { var name = ""; while (col > 0) { col--; name = (char)('A' + col % 26) + name; col /= 26; } return name + row; }
    static string PdfEscape(string s) => new string(s.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    async Task CreateTankAsync()
    {
        try
        {
            await EnsureClientAsync(); var product = _product.SelectedItem as Choice;
            if (product is null || string.IsNullOrWhiteSpace(_tankNumber.Text) || Decimal(_tankCapacity) <= 0) { SetStatus("Select a product and enter a tank number and positive working capacity.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/tanks", new { number = _tankNumber.Text.Trim(), productId = product.Id, capacityLitres = Decimal(_tankCapacity) });
            await ShowResultAsync(response, "Tank created. Next, assign a pump to it.");
            if (response.IsSuccessStatusCode) { _tankNumber.Text = ""; _tankCapacity.Text = ""; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task CreatePumpAsync()
    {
        try
        {
            await EnsureClientAsync(); var tank = _setupTank.SelectedItem as Choice;
            if (tank is null || string.IsNullOrWhiteSpace(_pumpNumber.Text)) { SetStatus("Select the tank and enter a pump number.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/pumps", new { number = _pumpNumber.Text.Trim(), tankId = tank.Id });
            await ShowResultAsync(response, "Pump created and assigned to its tank.");
            if (response.IsSuccessStatusCode) { _pumpNumber.Text = ""; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task RecordDeliveryAsync()
    {
        try
        {
            await EnsureClientAsync(); var tank = _receiptTank.SelectedItem as Choice;
            if (tank is null || Decimal(_receivedQuantity) <= 0 || Decimal(_unitCost) < 0) { SetStatus("Select a tank and enter a positive received quantity and a non-negative unit cost.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/fuel-receipts", new { tankId = tank.Id, invoiceNumber = _invoiceNumber.Text, invoiceLitres = 0m, receivedLitres = Decimal(_receivedQuantity), unitCost = Decimal(_unitCost), receiptDate = DateOnly.FromDateTime(DateTime.Today), receiptType = "Opening stock" });
            await ShowResultAsync(response, "Opening delivery recorded and costed.");
            if (response.IsSuccessStatusCode) { _invoiceNumber.Text = ""; _receivedQuantity.Text = ""; _unitCost.Text = ""; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task ReconcileReceiptShortAsync()
    {
        try
        {
            await EnsureClientAsync(); var receipt = _shortReceipt.SelectedItem as Choice;
            if (receipt is null || _shortPartyType.SelectedItem is not string partyType || string.IsNullOrWhiteSpace(_shortPartyName.Text)) { SetStatus("Choose a short delivery, supplier or driver, and enter their name.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/fuel-receipts/{receipt.Id}/short-reconciliation", new { partyType, partyName = _shortPartyName.Text.Trim(), reconciledLitres = Decimal(_shortSettledLitres), reconciledCost = Decimal(_shortSettledCost), stationBorneLitres = Decimal(_shortStationLitres), stationBorneCost = Decimal(_shortStationCost), disputed = _shortDisputed.IsChecked, settlementDate = DateOnly.FromDateTime(DateTime.Today) });
            await ShowResultAsync(response, "Delivery shortage reconciliation saved.");
            if (response.IsSuccessStatusCode) { _shortPartyName.Text = ""; _shortSettledLitres.Text = ""; _shortSettledCost.Text = ""; _shortStationLitres.Text = ""; _shortStationCost.Text = ""; _shortDisputed.IsChecked = false; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task CreateAttendantAsync()
    {
        try
        {
            await EnsureClientAsync();
            var salary = Decimal(_attendantSalary);
            if (string.IsNullOrWhiteSpace(_attendantName.Text) || salary < 0) { SetStatus("Enter an attendant name and a non-negative monthly salary.", true); return; }
            int? payday = null;
            if (!string.IsNullOrWhiteSpace(_attendantPayday.Text))
            {
                if (!int.TryParse(_attendantPayday.Text, out var day) || day is < 1 or > 31) { SetStatus("Salary payday must be a day from 1 to 31.", true); return; }
                payday = day;
            }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/attendants", new { name = _attendantName.Text.Trim(), phone = _attendantPhone.Text, dateJoined = DateOnly.FromDateTime(_attendantDateJoined.Date ?? DateTime.Today), monthlySalary = salary, salaryPaydayDay = payday });
            await ShowResultAsync(response, "Attendant created and ready for session assignment.");
            if (response.IsSuccessStatusCode) { _attendantName.Text = ""; _attendantPhone.Text = ""; _attendantSalary.Text = ""; _attendantPayday.Text = ""; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task CreateAccountAsync()
    {
        try
        {
            await EnsureClientAsync(); var name = _mobileAccountName.Text?.Trim() ?? ""; var type = _mobileAccountType.SelectedItem?.ToString() ?? ""; var opening = Decimal(_mobileOpeningBalance);
            if (name.Length < 2 || opening < 0) { SetStatus("Enter an account name and a non-negative opening balance.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/accounts", new { name, accountType = type, accountNumber = _mobileAccountNumber.Text, provider = _mobileAccountProvider.Text, openingBalance = opening });
            await ShowResultAsync(response, "Account created.");
            if (response.IsSuccessStatusCode) { _mobileAccountName.Text = _mobileAccountNumber.Text = _mobileAccountProvider.Text = _mobileOpeningBalance.Text = ""; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task SaveAccountTransactionAsync()
    {
        try
        {
            await EnsureClientAsync(); var account = _mobileAccount.SelectedItem as Choice; var type = _mobileTransactionType.SelectedItem?.ToString() ?? "";
            if (account is null) { SetStatus("Select the account for this transaction.", true); return; }
            var target = _mobileTargetAccount.SelectedItem as Choice; var receipt = _mobileReceipt.SelectedItem as Choice;
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/account-transactions", new { transactionType = type, amount = Decimal(_mobileTransactionAmount), accountId = account.Id, targetAccountId = target?.Id > 0 ? target.Id : (long?)null, fuelReceiptId = type == "Product Payment" ? receipt?.Id : (long?)null, reference = _mobileTransactionReference.Text, description = "Posted from mobile accounts", transactionDate = DateOnly.FromDateTime(DateTime.Today) });
            await ShowResultAsync(response, $"{type} recorded.");
            if (response.IsSuccessStatusCode) { _mobileTransactionAmount.Text = _mobileTransactionReference.Text = ""; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task RefreshMobileReconciliationAsync()
    {
        try
        {
            await EnsureClientAsync(); var date = DateOnly.FromDateTime(_reconciliationDate.Date ?? DateTime.Today);
            var item = await _client!.GetFromJsonAsync<JsonElement>($"api/v1/branches/{BranchId}/account-reconciliation?date={date:yyyy-MM-dd}");
            _mobileReconciliationSummary.Text = $"{date:yyyy-MM-dd} · Expected cash {Money(item.GetProperty("expectedCash"))} · Expected POS {Money(item.GetProperty("expectedPos"))}\nActual cash/POS {Money(item.GetProperty("cashCollected"))} / {Money(item.GetProperty("posCollected"))} · Vault count {(item.GetProperty("cashVaultCount").ValueKind == JsonValueKind.Number ? Money(item.GetProperty("cashVaultCount")) : "not counted")}\nCash pending {Money(item.GetProperty("cashPending"))} · POS pending {Money(item.GetProperty("posPending"))} · Status {item.GetProperty("status").GetString()}";
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task RecordMobileVaultCountAsync()
    {
        try
        {
            await EnsureClientAsync(); var client = _client ?? throw new InvalidOperationException("Sign in first."); var accounts = await client.GetFromJsonAsync<List<JsonElement>>($"api/v1/branches/{BranchId}/accounts") ?? [];
            var cash = accounts.FirstOrDefault(x => x.GetProperty("accountType").GetString() == "CASH");
            if (cash.ValueKind != JsonValueKind.Object) { SetStatus("Create a CASH account before recording a vault count.", true); return; }
            var date = DateOnly.FromDateTime(_reconciliationDate.Date ?? DateTime.Today); var amount = Decimal(_vaultCountAmount);
            var response = await client.PostAsJsonAsync($"api/v1/branches/{BranchId}/account-transactions", new { transactionType = "Cash Count", amount, accountId = cash.GetProperty("id").GetInt64(), targetAccountId = (long?)null, fuelReceiptId = (long?)null, reference = "Daily physical cash count", description = "Recorded from mobile cash vault", transactionDate = date });
            await ShowResultAsync(response, $"Cash vault count recorded · ₦{amount:N2}.");
            if (response.IsSuccessStatusCode) { _vaultCountAmount.Text = ""; await LoadChoicesAsync(); await RefreshMobileReconciliationAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task SaveExpenseAsync()
    {
        try
        {
            await EnsureClientAsync(); var expenseAccount = _expenseAccountPicker.SelectedItem as Choice; var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/expenses", new { description = _expenseDescription.Text, category = _expenseCategory.Text, amount = Decimal(_expenseAmount), expenseAt = DateTimeOffset.Now, sourceAccountId = expenseAccount?.Id > 0 ? expenseAccount.Id : (long?)null, notes = "Captured from mobile" });
            await ShowResultAsync(response, "Expense saved."); if (response.IsSuccessStatusCode) { _expenseDescription.Text = ""; _expenseAmount.Text = ""; await LoadDashboardAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task SaveDipAsync()
    {
        try
        {
            await EnsureClientAsync(); var tank = _tank.SelectedItem as Choice; if (tank is null) { SetStatus("Select a tank first.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/tank-dips", new { tankId = tank.Id, openingDip = Decimal(_dipOpening), closingDip = Decimal(_dip), notes = "Mobile opening baseline / stock check" });
            await ShowResultAsync(response, "Tank opening / closing stock saved."); if (response.IsSuccessStatusCode) { _dip.Text = ""; _dipOpening.Text = ""; await LoadDipOpeningAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task LoadDipOpeningAsync()
    {
        try
        {
            if (_tank.SelectedItem is not Choice tank) return;
            await EnsureClientAsync();
            var dips = await _client!.GetFromJsonAsync<List<JsonElement>>($"api/v1/branches/{BranchId}/tank-dips") ?? [];
            var previous = dips.Where(x => x.GetProperty("tankId").GetInt64() == tank.Id)
                .OrderByDescending(x => x.TryGetProperty("recordedAt", out var at) ? at.GetDateTimeOffset() : DateTimeOffset.MinValue)
                .FirstOrDefault();
            _dipOpening.Text = previous.ValueKind == JsonValueKind.Object && previous.TryGetProperty("closingDip", out var closing)
                ? closing.GetDecimal().ToString("0.##", CultureInfo.InvariantCulture) : "";
            _dipOpening.Placeholder = previous.ValueKind == JsonValueKind.Object ? "Opening stock · carried forward" : "Opening baseline · enter measured starting stock";
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task SaveProductSaleAsync()
    {
        try
        {
            await EnsureClientAsync(); var tank = _lpgTank.SelectedItem as Choice; if (tank is null) { SetStatus("Select an LPG tank first.", true); return; }
            var count = int.TryParse(_cylinderCount.Text, out var parsed) && parsed > 0 ? parsed : (int?)null;
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/product-sales", new { tankId = tank.Id, quantity = Decimal(_saleQuantity), unitPrice = Decimal(_salePrice), cashCollected = Decimal(_saleCash), posAmount = Decimal(_salePos), expenses = 0m, cylinderCount = count, cylinderSize = (string?)null, saleDate = DateOnly.FromDateTime(DateTime.Today) });
            await ShowResultAsync(response, "LPG fill posted."); if (response.IsSuccessStatusCode) { _saleQuantity.Text = ""; _salePrice.Text = ""; await LoadDashboardAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task SaveSessionAsync()
    {
        try
        {
            await EnsureClientAsync(); var pump = _pump.SelectedItem as Choice; var attendant = _attendant.SelectedItem as Choice; var run = _run.SelectedItem as Choice;
            if (pump is null || attendant is null || run is null) { SetStatus("Select a pump, attendant and open run.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/pump-sessions", new { pumpId = pump.Id, attendantId = attendant.Id, runId = run.Id, clientOperationId = Guid.NewGuid().ToString("N"), openingMeter = Decimal(_openMeter), closingMeter = Decimal(_closeMeter), pricePerLitre = Decimal(_price), cashCollected = Decimal(_cash), posAmount = Decimal(_pos), expenses = Decimal(_sessionExpense), rttLitres = Decimal(_rtt), sessionDate = DateOnly.FromDateTime(DateTime.Today) });
            await ShowResultAsync(response, "Pump session posted."); if (response.IsSuccessStatusCode) await LoadDashboardAsync();
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task OpenRunAsync()
    {
        try
        {
            await EnsureClientAsync(); var pump = _pump.SelectedItem as Choice; if (pump is null) { SetStatus("Select a pump first.", true); return; }
            var response = await _client!.PostAsJsonAsync($"api/v1/branches/{BranchId}/pump-runs", new { pumpId = pump.Id, openingMeter = Decimal(_runOpening) });
            await ShowResultAsync(response, "Pump run opened."); if (response.IsSuccessStatusCode) { _runOpening.Text = ""; await LoadChoicesAsync(); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    async Task ShowResultAsync(HttpResponseMessage response, string success) => SetStatus(response.IsSuccessStatusCode ? success : $"Could not save ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}", !response.IsSuccessStatusCode);
    long BranchId => _branch.SelectedItem is Choice branch ? branch.Id : 0;
    static decimal Decimal(Entry entry) => decimal.TryParse(entry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    static string Money(JsonElement value) => $"₦{value.GetDecimal():N2}";
    static Entry Numeric(string placeholder) => new() { Placeholder = placeholder, Keyboard = Keyboard.Numeric };
    void SetStatus(string message, bool error = false) { _status.Text = message; _status.TextColor = Color.FromArgb(error ? "#FFB2A5" : "#98AAA4"); }
    static Border Card(string heading, params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 11 };
        stack.Children.Add(new Label { Text = heading, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#E8B957") });
        foreach (var child in children) stack.Children.Add(child);
        return new Border { Stroke = Color.FromArgb("#344540"), StrokeThickness = 1, BackgroundColor = Color.FromArgb("#1A2926"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Padding = 18, Content = stack };
    }
    sealed record LoginResult(string AccessToken, string[]? Roles, BranchRole[]? Branches);
    sealed record BranchRole(long BranchId, string Role);
    sealed record Choice(long Id, string Label, decimal? OpeningMeter = null) { public override string ToString() => Label; }
}
