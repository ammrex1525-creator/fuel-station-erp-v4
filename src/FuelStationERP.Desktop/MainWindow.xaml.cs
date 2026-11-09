using System.Globalization;
using System.IO;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Polygon = System.Windows.Shapes.Polygon;
using Line = System.Windows.Shapes.Line;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using System.Net.Mail;
using System.Diagnostics;
using Microsoft.Win32;
using FuelStationERP.Domain.Entities;
using FuelStationERP.Domain.Services;
using FuelStationERP.Infrastructure.Persistence;
using FuelStationERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.Desktop;

public partial class MainWindow : Window
{
    private ErpDbContext? _db;
    private long _branchId;
    private bool _usingSharedDatabase;
    private readonly List<DataGrid> _grids = [];
    private ComboBox _sessionPump = null!, _sessionStaff = null!, _receiptTank = null!, _lpgTank = null!,
        _lpgStaff = null!, _dipTank = null!, _pumpTank = null!, _pumpProduct = null!,
        _cashSession = null!, _expenseAccount = null!, _transactionAccount = null!, _transactionTarget = null!, _transactionReceipt = null!, _transactionType = null!, _receiptType = null!;
    private TextBox _openingMeter = null!;
    private readonly TextBox _sessionDate = DateField();
    private readonly TextBox _receiptDate = DateField();
    private readonly TextBox _lpgDate = DateField();
    private readonly TextBox _dipDate = DateField();
    private readonly TextBox _expenseDate = DateField();
    private readonly TextBox _transactionDate = DateField();
    private readonly TextBox _sessionClosing = Field("Closing meter");
    private readonly TextBox _sessionPrice = Field("Selling price per unit");
    private readonly TextBox _sessionCash = Field("Cash collected");
    private readonly TextBox _sessionPos = Field("POS collected");
    private readonly TextBox _sessionExpense = Field("Session expenses");
    private readonly TextBox _sessionRtt = Field("RTT returned to tank");
    private readonly TextBox _supplier = Field("Supplier");
    private readonly TextBox _invoice = Field("Invoice number");
    private readonly TextBox _deliveryNote = Field("Delivery note");
    private readonly TextBox _vehicle = Field("Delivery vehicle");
    private readonly TextBox _driver = Field("Driver");
    private readonly TextBox _driverPhone = Field("Driver phone");
    private readonly TextBox _invoiceQty = Field("Invoice quantity");
    private readonly TextBox _receivedQty = Field("Quantity received");
    private readonly TextBox _receiptCost = Field("Cost per unit");
    private readonly TextBox _lpgQty = Field("Quantity filled (kg)");
    private readonly TextBox _lpgPrice = Field("Price per kg");
    private readonly TextBox _lpgCash = Field("Cash received");
    private readonly TextBox _lpgPos = Field("POS received");
    private readonly TextBox _lpgExpenses = Field("Sale expenses");
    private readonly TextBox _cylinderCount = Field("Cylinder count (optional)");
    private readonly TextBox _cylinderSize = Field("Cylinder size (optional, e.g. 6kg)");
    private readonly TextBox _dipOpening = Field("Opening stock baseline");
    private readonly TextBox _dipActual = Field("Closing physical stock");
    private readonly TextBox _expenseDescription = Field("Expense description");
    private readonly TextBox _expenseCategory = Field("Category");
    private readonly TextBox _expenseAmount = Field("Amount");
    private readonly TextBox _accountName = Field("Account name");
    private readonly TextBox _accountOpeningBalance = Field("Opening balance (optional)");
    private ComboBox _accountTypePicker = null!;
    private readonly TextBox _accountNumber = Field("Account number (optional)");
    private readonly TextBox _accountProvider = Field("Bank / POS provider (optional)");
    private readonly TextBox _transactionAmount = Field("Amount");
    private readonly TextBox _transactionReference = Field("Reference");
    private readonly TextBox _transactionDescription = Field("Description");
    private readonly TextBox _cashCollected = Field("Cash collected");
    private readonly TextBox _cashPos = Field("POS collected");
    private readonly TextBox _cashVaultCount = Field("Physical cash vault count");
    private readonly TextBox _tankNumber = Field("Tank number");
    private readonly TextBox _tankCapacity = Field("Capacity in product unit");
    private readonly TextBox _pumpNumber = Field("Pump number");
    private readonly TextBox _staffName = Field("Staff / attendant name");
    private readonly TextBox _staffPhone = Field("Phone number");
    private readonly TextBox _staffJoinDate = DateField();
    private readonly TextBox _staffSalary = Field("Monthly salary (NGN)");
    private readonly TextBox _staffPayday = Field("Salary payday (1–31, optional)");
    private readonly TextBox _accountReconcileDate = DateField();
    private readonly TextBox _productCode = Field("Product code");
    private readonly TextBox _productName = Field("Product name");
    private readonly TextBox _productUnit = Field("Unit: L, kg, cylinder, etc.");
    private readonly TextBox _reportFrom = DateField();
    private readonly TextBox _reportTo = DateField();
    private ComboBox _priceProduct = null!;
    private readonly TextBox _priceAmount = Field("Selling price per unit");
    private readonly TextBox _priceEffectiveDate = DateField();
    private DataGrid _priceHistoryGrid = null!;
    private TextBlock _sessionPriceStatus = null!;
    private ComboBox _shortReceipt = null!, _shortPartyType = null!;
    private readonly TextBox _shortPartyName = Field("Responsible supplier / driver");
    private readonly TextBox _shortReconciledLitres = Field("Shortage settled (unit)");
    private readonly TextBox _shortReconciledCost = Field("Settled cost (NGN)");
    private readonly TextBox _shortStationLitres = Field("Station-borne shortage (unit)");
    private readonly TextBox _shortStationCost = Field("Station-borne cost (NGN)");
    private readonly TextBox _shortSettlementDate = DateField();
    private CheckBox _shortDisputed = null!;
    private DataGrid _receiptSettlementGrid = null!;
    private DataGrid _overviewGrid = null!, _sessionGrid = null!, _receiptGrid = null!, _lpgGrid = null!,
        _dipGrid = null!, _expenseGrid = null!, _cashGrid = null!, _setupGrid = null!, _reportGrid = null!;
    private DataGrid _accountBalanceGrid = null!;
    private readonly Dictionary<string, DataGrid> _reportTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ReportRow>> _reportCategoryRows = new(StringComparer.OrdinalIgnoreCase);
    private TabControl _reportTabs = null!;
    private TextBox _reportEmailSender = null!, _reportEmailRecipient = null!;
    private CheckBox _reportEmailEnabled = null!;
    private TextBlock _reportEmailStatus = null!;
    private ListBox _reportDocumentsList = null!;
    private TextBlock _reconciliationSummary = null!;
    private StackPanel _reportVisuals = null!;
    private StackPanel _overviewVisuals = null!;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeStationAsync();
    }

    private async Task InitializeStationAsync()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelStationERP");
            Directory.CreateDirectory(folder);
            var sharedConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Erp");
            _usingSharedDatabase = !string.IsNullOrWhiteSpace(sharedConnection);
            var optionsBuilder = new DbContextOptionsBuilder<ErpDbContext>();
            if (!string.IsNullOrWhiteSpace(sharedConnection)) optionsBuilder.UseSqlServer(sharedConnection);
            else optionsBuilder.UseSqlite($"Data Source={Path.Combine(folder, "station-v4.db")};Cache=Shared;");
            var options = optionsBuilder.Options;
            _db = new ErpDbContext(options);
            await _db.Database.EnsureCreatedAsync();
            await EnsureBaselineCostColumnAsync();
            await EnsureProductPriceSchemaAsync();
            if (string.IsNullOrWhiteSpace(sharedConnection)) await EnsureLocalColumnsAsync();
            var branch = await _db.Branches.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (branch is null)
            {
                branch = new Branch { Name = "Main Branch", Code = "MAIN" };
                _db.Branches.Add(branch);
                _db.Products.AddRange(
                    new FuelProduct { Code = "PMS", Name = "Petrol (PMS)", UnitOfMeasure = "L" },
                    new FuelProduct { Code = "AGO", Name = "Diesel (AGO)", UnitOfMeasure = "L" },
                    new FuelProduct { Code = "LPG", Name = "Liquefied Petroleum Gas", UnitOfMeasure = "kg", IsMetered = false });
                _db.Accounts.AddRange(
                    new Account { BranchId = 1, Name = "Cash Till", AccountType = "CASH" },
                    new Account { BranchId = 1, Name = "POS Clearing", AccountType = "POS" });
                await _db.SaveChangesAsync();
            }
            _branchId = branch.Id;
            await LoadBranchesAsync();
            BranchSelector.SelectionChanged += async (_, _) =>
            {
                if (BranchSelector.SelectedValue is long selectedBranch && selectedBranch > 0 && selectedBranch != _branchId)
                {
                    _branchId = selectedBranch;
                    BranchLabel.Text = _usingSharedDatabase ? "Shared SQL Server data" : "Local branch data";
                    await RefreshAllAsync();
                }
            };
            AddBranchButton.Click += async (_, _) => await GuardAsync(AddBranchAsync);
            BranchLabel.Text = _usingSharedDatabase ? "Shared SQL Server data" : "Local branch data";
            BuildWorkspace();
            await RefreshAllAsync();
            SetStatus(string.IsNullOrWhiteSpace(sharedConnection) ? $"Ready · Database stored in {Path.Combine(folder, "station-v4.db")}" : "Ready · Connected to shared station database");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Station setup could not be opened.\n\n{ex.GetBaseException().Message}",
                "FuelStationERP", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Database startup failed");
        }
    }

    private void BuildWorkspace()
    {
        _overviewGrid = BuildOverview();
        _sessionGrid = BuildSessions();
        _receiptGrid = BuildReceipts();
        _lpgGrid = BuildLpgSales();
        _dipGrid = BuildDips();
        _expenseGrid = BuildExpenses();
        _cashGrid = BuildAccounts();
        _setupGrid = BuildSetup();
        _reportGrid = BuildReports();
    }

    private async Task EnsureLocalColumnsAsync()
    {
        var connection = Db().Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        foreach (var (table, column, declaration) in new[]
        {
            ("FuelReceipts", "UnitOfMeasureSnapshot", "TEXT NOT NULL DEFAULT 'L'"),
            ("FuelReceipts", "VehicleNumber", "TEXT NULL"),
            ("FuelReceipts", "DriverName", "TEXT NULL"),
            ("FuelReceipts", "DriverPhone", "TEXT NULL"),
            ("PumpSessions", "UnitOfMeasureSnapshot", "TEXT NOT NULL DEFAULT 'L'"),
            ("TankDips", "BaselineCost", "TEXT NULL")
        })
        {
            await using var check = connection.CreateCommand();
            check.CommandText = $"PRAGMA table_info(\"{table}\")";
            await using var reader = await check.ExecuteReaderAsync();
            var found = false;
            while (await reader.ReadAsync())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) found = true;
            await reader.DisposeAsync();
            if (found) continue;
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {declaration}";
            await alter.ExecuteNonQueryAsync();
        }
    }

    private async Task EnsureBaselineCostColumnAsync()
    {
        if (Db().Database.IsSqlite()) return; // Added to existing local databases by EnsureLocalColumnsAsync.
        await Db().Database.ExecuteSqlRawAsync("IF COL_LENGTH(N'dbo.TankDips', N'BaselineCost') IS NULL ALTER TABLE [TankDips] ADD [BaselineCost] decimal(19,4) NULL;");
    }

    private async Task EnsureProductPriceSchemaAsync()
    {
        if (Db().Database.IsSqlite())
        {
            await Db().Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ProductPrices (Id INTEGER NOT NULL CONSTRAINT PK_ProductPrices PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL, UnitPrice TEXT NOT NULL, EffectiveDate TEXT NOT NULL);");
            await Db().Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductPrices_ProductId_EffectiveDate ON ProductPrices (ProductId, EffectiveDate);");
        }
        else
        {
            await Db().Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'[ProductPrices]', N'U') IS NULL CREATE TABLE [ProductPrices] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ProductPrices] PRIMARY KEY, [ProductId] bigint NOT NULL, [UnitPrice] decimal(19,4) NOT NULL, [EffectiveDate] date NOT NULL);");
            await Db().Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductPrices_ProductId_EffectiveDate' AND object_id=OBJECT_ID(N'[ProductPrices]')) CREATE UNIQUE INDEX [IX_ProductPrices_ProductId_EffectiveDate] ON [ProductPrices] ([ProductId], [EffectiveDate]);");
        }
    }

    private DataGrid BuildOverview()
    {
        var host = OverviewHost;
        host.Children.Clear();
        host.Children.Add(Heading("Today at the station", "Live figures from posted sessions, deliveries, LPG fills and expenses."));
        var cards = new UniformGrid { Columns = 5, Margin = new Thickness(0, 12, 0, 18) };
        foreach (var (title, accent) in new[] { ("Pump sales", "#155EEF"), ("Other product sales", "#F58220"), ("Gross profit", "#12B76A"), ("Expenses", "#7F56D9"), ("Cash short / over", "#D92D20") })
        {
            var border = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(9), Padding = new Thickness(18), Margin = new Thickness(5), BorderBrush = new SolidColorBrush(Color.FromRgb(234, 236, 240)), BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            stack.Children.Add(new Border { Height = 4, Background = (Brush)new BrushConverter().ConvertFromString(accent)!, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 13) });
            stack.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("Muted"), FontSize = 13 });
            var metricName = title switch { "Pump sales" => "MetricPumpSales", "Other product sales" => "MetricLPGfills", "Gross profit" => "MetricGrossProfit", "Expenses" => "MetricExpenses", _ => "MetricShortOver" };
            stack.Children.Add(new TextBlock { Name = metricName, Text = "₦0", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Ink"), Margin = new Thickness(0, 7, 0, 0) });
            border.Child = stack; cards.Children.Add(border);
        }
        host.Children.Add(cards);
        _overviewVisuals = new StackPanel { Margin = new Thickness(5, 0, 5, 12) }; host.Children.Add(_overviewVisuals);
        host.Children.Add(new TextBlock { Text = "Recent station activity", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Ink"), Margin = new Thickness(5, 8, 0, 0) });
        var grid = GridView(); host.Children.Add(grid);
        return grid;
    }

    private DataGrid BuildSessions()
    {
        var form = FormArea(SessionsHost, "Pump sessions", "Meter readings are carried forward from the previous posted session. Short / over = cash + POS − (sales − session expenses).");
        _sessionPump = Picker(); _sessionPump.Tag = "Pump"; _sessionStaff = Picker(optional: true); _sessionStaff.Tag = "Attendant (optional)";
        _openingMeter = Field("Opening meter (carried forward)");
        _openingMeter.IsReadOnly = true;
        _sessionPrice.IsReadOnly = true;
        _sessionPump.SelectionChanged += async (_, _) => await LoadOpeningMeterAsync();
        _sessionDate.LostFocus += async (_, _) => await LoadEffectiveSessionPriceAsync();
        AddFields(form, _sessionPump, _sessionStaff, _openingMeter, _sessionClosing, _sessionPrice, _sessionCash, _sessionPos, _sessionExpense, _sessionRtt, _sessionDate);
        _sessionPriceStatus = new TextBlock { Text = "Selling price loads automatically from the effective-dated price book.", Margin = new Thickness(7, 2, 7, 7), Foreground = (Brush)FindResource("Muted") }; form.Children.Add(_sessionPriceStatus);
        AddAction(form, "Post pump session (FIFO)", async () => await PostPumpSessionAsync());
        _sessionGrid = AddGrid(SessionsHost); return _sessionGrid;
    }

    private DataGrid BuildReceipts()
    {
        var form = FormArea(ReceiptsHost, "Fuel deliveries", "Keep invoice quantity separate from the quantity actually received. Received fuel becomes an auditable FIFO cost batch.");
        _receiptTank = Picker(); _receiptTank.Tag = "Tank"; _receiptType = Picker(); _receiptType.ItemsSource = new[] { "Delivery", "Opening Balance" }; _receiptType.SelectedIndex = 0; _receiptType.DisplayMemberPath = null; _receiptType.SelectedValuePath = null;
        _receiptType.Tag = "Receipt type";
        AddFields(form, _receiptType, _receiptTank, _supplier, _invoice, _deliveryNote, _vehicle, _driver, _driverPhone, _invoiceQty, _receivedQty, _receiptCost, _receiptDate);
        AddAction(form, "Save delivery", async () => await SaveReceiptAsync());
        var settlement = new StackPanel { Margin = new Thickness(4, 4, 4, 8) };
        settlement.Children.Add(Subheading("Delivery shortage reconciliation"));
        settlement.Children.Add(new TextBlock { Text = "Record how an invoice shortage was settled by the supplier or driver, or mark the part absorbed by the station.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 0, 5, 6) });
        _shortReceipt = Picker(); _shortReceipt.Tag = "Fuel receipt / delivery";
        _shortPartyType = Picker(); _shortPartyType.Tag = "Responsible party"; _shortPartyType.ItemsSource = new[] { "Supplier", "Driver" }; _shortPartyType.SelectedIndex = 0;
        _shortDisputed = new CheckBox { Content = "Disputed", Margin = new Thickness(6, 10, 6, 5) };
        _shortSettlementDate.Tag = "Settlement date (YYYY-MM-DD)";
        AddFields(settlement, _shortReceipt, _shortPartyType, _shortPartyName, _shortReconciledLitres, _shortReconciledCost, _shortStationLitres, _shortStationCost, _shortSettlementDate);
        settlement.Children.Add(_shortDisputed);
        AddAction(settlement, "Save shortage reconciliation", async () => await SaveReceiptShortReconciliationAsync());
        _receiptSettlementGrid = GridView(); _receiptSettlementGrid.Height = 120; settlement.Children.Add(_receiptSettlementGrid);
        ((StackPanel)ReceiptsHost.Children[0]).Children.Add(settlement);
        _receiptGrid = AddGrid(ReceiptsHost); return _receiptGrid;
    }

    private DataGrid BuildLpgSales()
    {
        var form = FormArea(LpgHost, "LPG fills and sales", "Record LPG by weight, price per kg and optional cylinder details. LPG is not entered as a pump-meter reading.");
        _lpgTank = Picker(); _lpgTank.Tag = "LPG tank"; _lpgStaff = Picker(optional: true); _lpgStaff.Tag = "Attendant (optional)";
        _lpgPrice.IsReadOnly = true;
        _lpgTank.SelectionChanged += async (_, _) => await LoadEffectiveLpgPriceAsync();
        _lpgDate.LostFocus += async (_, _) => await LoadEffectiveLpgPriceAsync();
        AddFields(form, _lpgTank, _lpgStaff, _lpgQty, _lpgPrice, _lpgCash, _lpgPos, _lpgExpenses, _cylinderCount, _cylinderSize, _lpgDate);
        AddAction(form, "Post LPG fill / sale (FIFO)", async () => await PostLpgSaleAsync());
        _lpgGrid = AddGrid(LpgHost); return _lpgGrid;
    }

    private DataGrid BuildDips()
    {
        var form = FormArea(DipsHost, "Tank stock checks", "Record opening baseline and closing physical stock. Expected closing = opening + deliveries − sales. Tank loss / gain = expected closing − physical closing. A positive result is a loss; a negative result is a gain. The first check establishes the baseline.");
        _dipTank = Picker(); _dipTank.Tag = "Tank"; _dipTank.SelectionChanged += async (_, _) => await LoadDipOpeningAsync(); AddFields(form, _dipTank, _dipOpening, _dipActual, _dipDate);
        AddAction(form, "Record stock check", async () => await SaveDipAsync());
        AddAction(form, "Product Finished · close all pumps on this tank", async () => await FinishTankProductAsync());
        _dipGrid = AddGrid(DipsHost); return _dipGrid;
    }

    private DataGrid BuildExpenses()
    {
        var form = FormArea(ExpensesHost, "Operating expenses", "Record station expenses separately from pump-session deductions.");
        _expenseAccount = Picker(optional: true); AddFields(form, _expenseDescription, _expenseCategory, _expenseAmount, _expenseAccount, _expenseDate);
        AddAction(form, "Save expense", async () => await SaveExpenseAsync());
        _expenseGrid = AddGrid(ExpensesHost); return _expenseGrid;
    }

    private DataGrid BuildAccounts()
    {
        var host = AccountsHost;
        var layout = PageLayout(host, "Cash collection and accounts", "Track session collections and account movements.");
        layout.Children.Add(Subheading("Selling prices · effective dated"));
        var priceForm = new StackPanel { Margin = new Thickness(4) };
        _priceProduct = Picker(); _priceProduct.Tag = "Product"; _priceEffectiveDate.Tag = "Price effective date (YYYY-MM-DD)";
        AddFields(priceForm, _priceProduct, _priceAmount, _priceEffectiveDate);
        AddAction(priceForm, "Save selling price", async () => await SaveProductPriceAsync());
        layout.Children.Add(priceForm);
        _priceHistoryGrid = GridView(); _priceHistoryGrid.Height = 110; layout.Children.Add(_priceHistoryGrid);
        layout.Children.Add(Subheading("Account balances"));
        _accountBalanceGrid = GridView(); _accountBalanceGrid.Height = 125; layout.Children.Add(_accountBalanceGrid);
        layout.Children.Add(Subheading("Daily cash reconciliation"));
        _accountReconcileDate.Tag = "Reconciliation date (YYYY-MM-DD)";
        AddFields(layout, _accountReconcileDate);
        _reconciliationSummary = new TextBlock { Text = "Choose a date and refresh to compare posted session sales with recorded collections.", Margin = new Thickness(7, 4, 7, 8), FontSize = 14, FontWeight = FontWeights.SemiBold };
        layout.Children.Add(_reconciliationSummary);
        AddAction(layout, "Refresh cash reconciliation", async () => await RefreshCashReconciliationAsync());
        var cashCount = new StackPanel { Margin = new Thickness(4) };
        cashCount.Children.Add(Subheading("Cash vault close"));
        AddFields(cashCount, _cashVaultCount);
        AddAction(cashCount, "Record physical vault count", async () => await SaveCashVaultCountAsync());
        layout.Children.Add(cashCount);
        var forms = new UniformGrid { Columns = 2 };
        layout.Children.Add(forms);
        var collect = new StackPanel { Margin = new Thickness(4) };
        collect.Children.Add(Subheading("Collect pump-session cash"));
        _cashSession = Picker();
        AddFields(collect, _cashSession, _cashCollected, _cashPos);
        AddAction(collect, "Record collection", async () => await SaveCashCollectionAsync());
        forms.Children.Add(collect);
        var txn = new StackPanel { Margin = new Thickness(4) };
        txn.Children.Add(Subheading("Account transaction"));
        _transactionType = Picker(); _transactionType.Tag = "Transaction type";
        _transactionType.ItemsSource = new[] { "Opening Balance", "Account Receipt", "Cash Count", "Cash Expense", "POS Transaction", "Account Expense", "Product Payment", "Account Payment / Amount Sent", "Bank Deposit", "POS Settlement", "Account Transfer" };
        _transactionType.SelectedIndex = 0;
        _transactionAccount = Picker(); _transactionAccount.Tag = "From / account";
        _transactionTarget = Picker(optional: true); _transactionTarget.Tag = "To account (transfers)";
        _transactionReceipt = Picker(optional: true); _transactionReceipt.Tag = "Unpaid delivery (product payment)";
        AddFields(txn, _transactionType, _transactionAmount, _transactionAccount, _transactionTarget, _transactionReceipt, _transactionReference, _transactionDescription, _transactionDate);
        AddAction(txn, "Save transaction", async () => await SaveAccountTransactionAsync());
        forms.Children.Add(txn);
        var account = new StackPanel { Margin = new Thickness(4) };
        account.Children.Add(Subheading("Set up an account"));
        _accountTypePicker = Picker(); _accountTypePicker.Tag = "Account type"; _accountTypePicker.ItemsSource = new[] { "CASH", "BANK", "POS" }; _accountTypePicker.SelectedIndex = 0;
        AddFields(account, _accountName, _accountTypePicker, _accountNumber, _accountProvider, _accountOpeningBalance);
        AddAction(account, "Add account", async () => await AddAccountAsync());
        forms.Children.Add(account);
        _cashGrid = AddGrid(host); return _cashGrid;
    }

    private DataGrid BuildSetup()
    {
        var host = SetupHost;
        var layout = PageLayout(host, "Station setup", "Complete the branch setup before posting sales. LPG is stored in a tank and sold by weight without a pump.");
        var instructions = new Border { Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)), BorderBrush = new SolidColorBrush(Color.FromRgb(191, 219, 254)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(4, 0, 4, 10) };
        instructions.Child = new TextBlock { Text = "1  Create a tank and select its product  →  2  Create a pump for a metered tank  →  3  Add an attendant  →  4  Record opening stock or a costed delivery", Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 120)), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        layout.Children.Add(instructions);
        var forms = new UniformGrid { Columns = 3 };
        layout.Children.Add(forms);
        var tanks = SetupCard("1 · Tank", "Choose the product stored in this tank and enter its working capacity in the product unit.");
        _pumpProduct = Picker(); _pumpProduct.Tag = "Product"; AddFields(tanks, _tankNumber, _pumpProduct, _tankCapacity);
        AddAction(tanks, "Create tank", async () => await AddTankAsync());
        forms.Children.Add(tanks);
        var pumps = SetupCard("2 · Pump", "Pumps dispense metered products. Assign each pump to one active tank in this branch.");
        _pumpTank = Picker(); _pumpTank.Tag = "Tank"; AddFields(pumps, _pumpNumber, _pumpTank);
        AddAction(pumps, "Create pump", async () => await AddPumpAsync());
        forms.Children.Add(pumps);
        var staff = SetupCard("3 · Attendant", "Create the station staff record used to assign pump sessions and LPG fills. Login access is managed separately under Team.");
        _staffJoinDate.Tag = "Date joined (YYYY-MM-DD, optional)"; _staffJoinDate.Text = "";
        _staffSalary.Text = "0";
        AddFields(staff, _staffName, _staffPhone, _staffJoinDate, _staffSalary, _staffPayday);
        AddAction(staff, "Add attendant", async () => await AddStaffAsync());
        forms.Children.Add(staff);
        _setupGrid = AddGrid(host); return _setupGrid;
    }

    private DataGrid BuildReports()
    {
        var form = FormArea(ReportsHost, "Report Center", "Choose a report category, filter by date, and export that report to PDF, Excel or CSV. Categories include deliveries, sales, stock, expenses and attendant activity.");
        _reportFrom.Text = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _reportTo.Text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _reportFrom.Tag = "From date (YYYY-MM-DD)"; _reportTo.Tag = "To date (YYYY-MM-DD)";
        AddFields(form, _reportFrom, _reportTo);
        var emailCard = SetupCard("Product-finished report email", "When Product Finished closes the pump runs for a tank, email a PDF delivery and sales report. SMTP credentials are read from CLEON_SMTP_* environment settings.");
        _reportEmailSender = Field("Sender email"); _reportEmailSender.Text = "ammarabdullahi3042@gmail.com";
        _reportEmailRecipient = Field("Recipient email"); _reportEmailRecipient.Text = "cleonoilandgas@gmail.com";
        _reportEmailEnabled = new CheckBox { Content = "Email on Product Finished", IsChecked = true, Margin = new Thickness(5) };
        _reportEmailStatus = new TextBlock { Text = "Email disabled until settings are saved.", Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted") };
        AddFields(emailCard, _reportEmailSender, _reportEmailRecipient, _reportEmailEnabled);
        AddAction(emailCard, "Save email settings", async () => await SaveReportEmailSettingsAsync());
        emailCard.Children.Add(_reportEmailStatus);
        form.Children.Add(emailCard);
        var help = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 247, 237)), BorderBrush = new SolidColorBrush(Color.FromRgb(254, 215, 170)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(4, 2, 4, 8) };
        help.Child = new TextBlock { Text = "FIFO cost of goods sold uses the oldest available matching delivery batches. Gross profit = fuel and LPG sales − FIFO cost of goods sold; operating expenses are shown as separate report rows.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(124, 45, 18)), FontSize = 13 };
        form.Children.Add(help);
        var actions = new WrapPanel { Margin = new Thickness(2, 0, 0, 8) }; form.Children.Add(actions);
        AddAction(actions, "Refresh report", async () => await LoadReportAsync());
        AddAction(actions, "Export PDF", ExportReportPdfAsync);
        AddAction(actions, "Export Excel (.xlsx)", ExportReportExcelAsync);
        AddAction(actions, "Export CSV", ExportReportCsvAsync);
        _reportVisuals = new StackPanel { Margin = new Thickness(5, 4, 5, 12) }; form.Children.Add(_reportVisuals);
        _reportTabs = new TabControl { Margin = new Thickness(4), MinHeight = 300 };
        foreach (var category in new[] { "Summary", "Product / Delivery", "Profit & Loss", "Sales", "Tank Stock", "Fuel Receipts", "Attendants", "Expenses", "RTT", "Documents" })
        {
            var content = new Grid();
            if (category == "Documents")
            {
                var documentPanel = new StackPanel { Margin = new Thickness(8) };
                documentPanel.Children.Add(new TextBlock { Text = "Report documents stored on this computer", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 4, 4, 8) });
                var documentActions = new WrapPanel();
                AddAction(documentActions, "Add document", async () => await AddReportDocumentAsync());
                AddAction(documentActions, "Open selected", async () => await OpenReportDocumentAsync());
                AddAction(documentActions, "Download selected", async () => await DownloadReportDocumentAsync());
                AddAction(documentActions, "Open documents folder", async () => await OpenReportDocumentsFolderAsync());
                documentPanel.Children.Add(documentActions);
                var documentList = new ListBox { Name = "ReportDocumentsList", MinHeight = 180, Margin = new Thickness(4) };
                content.Children.Add(documentPanel); Grid.SetRow(documentList, 1); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition()); content.Children.Add(documentList);
                _reportDocumentsList = documentList;
            }
            else
            {
                var grid = GridView(); grid.AutoGenerateColumns = true;
                content.Children.Add(grid); _reportTables[category] = grid;
                if (category == "Summary") _reportGrid = grid;
            }
            _reportTabs.Items.Add(new TabItem { Header = category, Content = content });
        }
        _reportTabs.SelectionChanged += (_, _) =>
        {
            if (_reportTabs.SelectedItem is TabItem tab && _reportTables.TryGetValue(tab.Header?.ToString() ?? "", out var grid)) _reportGrid = grid;
            if (_reportTabs.SelectedItem is TabItem documentsTab && documentsTab.Header?.ToString() == "Documents") _ = RefreshReportDocumentsAsync();
        };
        form.Children.Add(_reportTabs);
        LoadReportEmailSettings();
        return _reportGrid;
    }

    private async Task RefreshAllAsync()
    {
        if (_db is null) return;
        await FillPickerAsync(_sessionPump, await _db.Pumps.Where(x => x.BranchId == _branchId && x.IsActive).Select(x => new Choice(x.Id, x.Number)).ToListAsync());
        await FillPickerAsync(_pumpTank, await TankChoicesAsync(null));
        await FillPickerAsync(_receiptTank, await TankChoicesAsync(null));
        var receiptChoices = await _db.FuelReceipts.Where(x => x.BranchId == _branchId).OrderByDescending(x => x.Id)
            .Select(x => new Choice(x.Id, "Receipt #" + x.Id + " · " + x.ProductCodeSnapshot + " · invoice " + x.InvoiceLitres + " / received " + x.ReceivedLitres)).ToListAsync();
        await FillPickerAsync(_shortReceipt, receiptChoices);
        await FillPickerAsync(_dipTank, await TankChoicesAsync(null));
        await FillPickerAsync(_lpgTank, await TankChoicesAsync("LPG"));
        await FillPickerAsync(_pumpProduct, await _db.Products.Where(x => x.IsActive).Select(x => new Choice(x.Id, x.Code + " · " + x.UnitOfMeasure)).ToListAsync());
        await FillPickerAsync(_priceProduct, await _db.Products.Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new Choice(x.Id, x.Code + " · " + x.UnitOfMeasure)).ToListAsync());
        var staff = await _db.StaffAttendants.Where(x => x.BranchId == _branchId).Select(x => new Choice(x.Id, x.Name)).ToListAsync();
        await FillPickerAsync(_sessionStaff, staff, true); await FillPickerAsync(_lpgStaff, staff, true);
        var accounts = await _db.Accounts.Where(x => x.BranchId == _branchId && x.IsActive).Select(x => new Choice(x.Id, x.Name + " · " + x.AccountType)).ToListAsync();
        await FillPickerAsync(_expenseAccount, accounts, true); await FillPickerAsync(_transactionAccount, accounts);
        await FillPickerAsync(_transactionTarget, accounts, true);
        var receipts = await _db.FuelReceipts.Where(x => x.BranchId == _branchId).OrderBy(x => x.ReceiptDate).ThenBy(x => x.Id).ToListAsync();
        var payments = await _db.AccountTransactions.Where(x => x.BranchId == _branchId && x.TransactionType == "Product Payment").ToListAsync();
        var settlements = await _db.ReceiptShortSettlements.Where(x => x.BranchId == _branchId).ToListAsync();
        var unpaidReceipts = receipts.Select(r =>
        {
            var due = Math.Max(r.TotalCost + settlements.Where(s => s.FuelReceiptId == r.Id).Sum(s => s.StationBorneCost) - payments.Where(p => p.FuelReceiptId == r.Id).Sum(p => p.Amount), 0);
            return new Choice(r.Id, $"#{r.Id} · {r.ProductCodeSnapshot} · {r.SupplierNameSnapshot ?? "Supplier not set"} · due ₦{due:N2}");
        }).Where((_, index) =>
        {
            var r = receipts[index];
            return r.TotalCost + settlements.Where(s => s.FuelReceiptId == r.Id).Sum(s => s.StationBorneCost) - payments.Where(p => p.FuelReceiptId == r.Id).Sum(p => p.Amount) > 0.005m;
        }).ToList();
        await FillPickerAsync(_transactionReceipt, unpaidReceipts, true);
        var sessions = await _db.PumpSessions.Where(x => x.BranchId == _branchId).OrderByDescending(x => x.Id).Take(150)
            .Select(x => new Choice(x.Id, "Session #" + x.Id + " · " + x.SessionDate + " · ₦" + x.SaleAmount)).ToListAsync();
        await FillPickerAsync(_cashSession, sessions);
        await LoadGridsAsync();
    }

    private async Task LoadBranchesAsync()
    {
        if (_db is null) return;
        var choices = await _db.Branches.Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new Choice(x.Id, x.Name)).ToListAsync();
        BranchSelector.ItemsSource = choices;
        BranchSelector.DisplayMemberPath = nameof(Choice.Label);
        BranchSelector.SelectedValuePath = nameof(Choice.Id);
        BranchSelector.SelectedValue = _branchId;
    }

    private async Task AddBranchAsync()
    {
        var name = AskForText("Add a branch", "Branch or station name");
        if (string.IsNullOrWhiteSpace(name)) return;
        var db = Db();
        var baseCode = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(12).ToArray());
        if (baseCode.Length == 0) baseCode = "BRANCH";
        var code = baseCode; var suffix = 2;
        while (await db.Branches.AnyAsync(x => x.Code == code)) code = baseCode + suffix++;
        var branch = new Branch { Name = name.Trim(), Code = code };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        db.Accounts.AddRange(
            new Account { BranchId = branch.Id, Name = "Cash Till", AccountType = "CASH" },
            new Account { BranchId = branch.Id, Name = "POS Clearing", AccountType = "POS" });
        await db.SaveChangesAsync();
        _branchId = branch.Id;
        await LoadBranchesAsync();
        BranchSelector.SelectedValue = branch.Id;
        await RefreshAllAsync();
        SetStatus($"Branch added · {branch.Name}. Station products are shared; tanks, pumps, staff, stock and transactions are branch-specific.");
    }

    private string? AskForText(string title, string prompt)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 390, Height = 170, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(3) });
        var input = new TextBox { MinWidth = 280, Margin = new Thickness(3, 8, 3, 12) };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += (_, _) => dialog.DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Background = new SolidColorBrush(Color.FromRgb(102, 112, 133)) };
        buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private async Task<List<Choice>> TankChoicesAsync(string? code)
    {
        if (_db is null) return [];
        var query = from t in _db.Tanks join p in _db.Products on t.ProductId equals p.Id
                    where t.BranchId == _branchId && t.IsActive && (code == null || p.Code == code)
                    orderby t.Number select new Choice(t.Id, t.Number + " · " + p.Code + " (" + p.UnitOfMeasure + ")");
        return await query.ToListAsync();
    }

    private async Task LoadGridsAsync()
    {
        if (_db is null) return;
        _sessionGrid.ItemsSource = await (from s in _db.PumpSessions where s.BranchId == _branchId orderby s.Id descending
            select new { s.Id, s.SessionDate, Pump = s.PumpId, Product = s.ProductCodeSnapshot, Sold = s.LitresSold, Unit = s.UnitOfMeasureSnapshot,
                s.PricePerLitre, s.SaleAmount, s.CashCollected, s.PosAmount, s.Expenses, s.RttLitres, s.ShortOver, s.CostOfGoodsSold, s.GrossProfit }).Take(250).ToListAsync();
        _receiptGrid.ItemsSource = await (from r in _db.FuelReceipts where r.BranchId == _branchId orderby r.Id descending
            select new { r.Id, r.ReceiptDate, r.ProductCodeSnapshot, Unit = r.UnitOfMeasureSnapshot, r.SupplierNameSnapshot, r.InvoiceNumber, r.VehicleNumber, r.DriverName, r.DriverPhone, Invoice = r.InvoiceLitres,
                Received = r.ReceivedLitres, Shortage = r.InvoiceLitres > r.ReceivedLitres ? r.InvoiceLitres - r.ReceivedLitres : 0, Remaining = r.RemainingLitres, r.UnitCost, r.TotalCost }).Take(250).ToListAsync();
        _lpgGrid.ItemsSource = await _db.ProductSales.Where(x => x.BranchId == _branchId).OrderByDescending(x => x.Id).Take(250)
            .Select(x => new { x.Id, x.SaleDate, x.Quantity, x.UnitOfMeasureSnapshot, x.UnitPrice, x.SaleAmount, x.CylinderCount, x.CylinderSize, x.CashCollected, x.PosAmount, x.ShortOver, x.CostOfGoodsSold, x.GrossProfit }).ToListAsync();
        _dipGrid.ItemsSource = await (from d in _db.TankDips join t in _db.Tanks on d.TankId equals t.Id
            where d.BranchId == _branchId orderby d.Id descending select new { d.Id, Tank = t.Number, d.RecordedAt, Opening = d.OpeningDip, ExpectedClosing = d.ExpectedLitres, PhysicalClosing = d.ClosingDip, d.BaselineCost,
                LossGain = d.Notes == "Opening baseline" ? 0 : d.ExpectedLitres == null ? 0 : d.ExpectedLitres - d.ClosingDip,
                Result = d.Notes == "Opening baseline" ? "Baseline" : d.ExpectedLitres == null || d.ClosingDip == d.ExpectedLitres ? "Match" : d.ExpectedLitres > d.ClosingDip ? "Loss" : "Gain" }).Take(250).ToListAsync();
        _receiptSettlementGrid.ItemsSource = await (from s in _db.ReceiptShortSettlements join r in _db.FuelReceipts on s.FuelReceiptId equals r.Id
            where s.BranchId == _branchId orderby s.Id descending select new { s.Id, Receipt = s.FuelReceiptId, Product = r.ProductCodeSnapshot, s.PartyType, s.PartyName, Settled = s.ReconciledLitres, StationBorne = s.StationBorneLitres, s.Disputed, s.SettlementDate }).Take(100).ToListAsync();
        _expenseGrid.ItemsSource = await _db.Expenses.Where(x => x.BranchId == _branchId).OrderByDescending(x => x.Id).Take(250)
            .Select(x => new { x.Id, x.ExpenseAt, x.Description, x.Category, x.Amount }).ToListAsync();
        _cashGrid.ItemsSource = await _db.AccountTransactions.Where(x => x.BranchId == _branchId).OrderByDescending(x => x.Id).Take(250)
            .Select(x => new { x.Id, x.OccurredAt, x.TransactionType, x.Amount, x.Reference, x.Description }).ToListAsync();
        _priceHistoryGrid.ItemsSource = await (from pp in _db.ProductPrices join p in _db.Products on pp.ProductId equals p.Id orderby pp.EffectiveDate descending, p.Code select new { Product = p.Code, pp.UnitPrice, Unit = p.UnitOfMeasure, pp.EffectiveDate }).Take(100).ToListAsync();
        await RefreshAccountBalancesAsync();
        await RefreshCashReconciliationAsync();
        var setupRows = new List<SetupRow>();
        setupRows.AddRange(await _db.Products.OrderBy(x => x.Code).Select(x => new SetupRow("Product", x.Code, x.Name, x.UnitOfMeasure + (x.IsMetered ? " · metered" : " · weigh / package sale"))).ToListAsync());
        setupRows.AddRange(await (from t in _db.Tanks join p in _db.Products on t.ProductId equals p.Id where t.BranchId == _branchId
            select new SetupRow("Tank", t.Number, p.Code, $"{t.CapacityLitres:N2} {p.UnitOfMeasure} capacity")).ToListAsync());
        setupRows.AddRange(await (from p in _db.Pumps join t in _db.Tanks on p.TankId equals t.Id where p.BranchId == _branchId
            select new SetupRow("Pump", p.Number, t.Number, p.IsActive ? "Active" : "Inactive")).ToListAsync());
        var attendants = await _db.StaffAttendants.Where(x => x.BranchId == _branchId).Select(x => new { x.Name, x.Phone, x.DateJoined, x.MonthlySalary, x.SalaryPaydayDay }).ToListAsync();
        setupRows.AddRange(attendants.Select(x => new SetupRow("Attendant", x.Name, x.Phone ?? "", $"Joined {x.DateJoined?.ToString() ?? "—"} · Salary ₦{x.MonthlySalary:N2} · Payday {x.SalaryPaydayDay?.ToString() ?? "—"}")));
        _setupGrid.ItemsSource = setupRows;
        await LoadReportAsync();
        await RefreshMetricsAsync();
    }

    private async Task LoadReportAsync()
    {
        if (_db is null || _reportGrid is null) return;
        var from = Date(_reportFrom); var to = Date(_reportTo);
        if (from > to) throw new ArgumentException("The report start date must be on or before the end date.");
        var startLocal = from.ToDateTime(TimeOnly.MinValue);
        var endLocal = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var reportItems = new List<ReportRow>();
        var pumpSessions = await _db.PumpSessions.Where(x => x.BranchId == _branchId && x.SessionDate >= from && x.SessionDate <= to).ToListAsync();
        var attendants = await _db.StaffAttendants.Where(x => x.BranchId == _branchId).ToDictionaryAsync(x => x.Id, x => x.Name);
        var sessionRows = pumpSessions.Select(x => new ReportRow("Pump session", x.SessionDate, x.AttendantId is long aid && attendants.TryGetValue(aid, out var attendant) ? $"{x.ProductCodeSnapshot} · {attendant}" : x.ProductCodeSnapshot, x.LitresSold, x.UnitOfMeasureSnapshot, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.ShortOver)).ToList();
        reportItems.AddRange(sessionRows);
        reportItems.AddRange(pumpSessions.Where(x => x.Expenses > 0).Select(x => new ReportRow("Expense", x.SessionDate, "Pump session expense", 0, "", -x.Expenses, 0, -x.Expenses, 0)));
        var rttRows = pumpSessions.Where(x => x.RttLitres > 0).Select(x => new ReportRow("RTT returned", x.SessionDate, x.ProductCodeSnapshot, x.RttLitres, x.UnitOfMeasureSnapshot, 0, x.RttCost, -x.RttCost, 0)).ToList();
        var productSales = await _db.ProductSales.Where(x => x.BranchId == _branchId && x.SaleDate >= from && x.SaleDate <= to).ToListAsync();
        var saleRows = productSales.Select(x => new ReportRow("LPG fill", x.SaleDate, x.AttendantId is long aid && attendants.TryGetValue(aid, out var attendant) ? $"{x.ProductCodeSnapshot} · {attendant}" : x.ProductCodeSnapshot, x.Quantity, x.UnitOfMeasureSnapshot, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.ShortOver)).ToList();
        reportItems.AddRange(saleRows);
        reportItems.AddRange(productSales.Where(x => x.Expenses > 0).Select(x => new ReportRow("Expense", x.SaleDate, "LPG fill expense", 0, "", -x.Expenses, 0, -x.Expenses, 0)));
        var expenses = await _db.Expenses.Where(x => x.BranchId == _branchId).ToListAsync();
        var expenseRows = expenses.Where(x => x.ExpenseAt.LocalDateTime >= startLocal && x.ExpenseAt.LocalDateTime < endLocal)
            .Select(x => new ReportRow("Expense", DateOnly.FromDateTime(x.ExpenseAt.LocalDateTime), string.IsNullOrWhiteSpace(x.Category) ? x.Description : $"{x.Category} · {x.Description}", 0, "", -x.Amount, 0, -x.Amount, 0)).ToList();
        reportItems.AddRange(expenseRows);
        var receipts = await _db.FuelReceipts.Where(x => x.BranchId == _branchId && x.ReceiptDate >= from && x.ReceiptDate <= to).ToListAsync();
        var receiptRows = receipts.Select(x => new ReportRow("Fuel delivery", x.ReceiptDate, $"{x.ProductCodeSnapshot} · {x.SupplierNameSnapshot ?? "Supplier not set"} · Invoice {x.InvoiceNumber ?? "—"}", x.ReceivedLitres, x.UnitOfMeasureSnapshot, x.TotalCost, 0, 0, Math.Max(x.InvoiceLitres - x.ReceivedLitres, 0))).ToList();
        reportItems.AddRange(receiptRows);
        var tanks = await _db.Tanks.Where(x => x.BranchId == _branchId).ToListAsync();
        var dips = await _db.TankDips.Where(x => x.BranchId == _branchId).ToListAsync();
        var tankRows = dips.Where(x => DateOnly.FromDateTime(x.RecordedAt.LocalDateTime) >= from && DateOnly.FromDateTime(x.RecordedAt.LocalDateTime) <= to)
            .Select(x => new ReportRow(x.Notes?.Contains("opening baseline", StringComparison.OrdinalIgnoreCase) == true ? "Opening baseline" : "Tank stock check", DateOnly.FromDateTime(x.RecordedAt.LocalDateTime), $"Tank {tanks.FirstOrDefault(t => t.Id == x.TankId)?.Number ?? x.TankId.ToString()} · {(x.Notes?.Contains("opening baseline", StringComparison.OrdinalIgnoreCase) == true ? "baseline cost excluded from profit" : "expected closing − physical closing")}", x.ClosingDip, "L / product unit", x.BaselineCost ?? 0, x.ExpectedLitres ?? 0, x.Notes?.Contains("opening baseline", StringComparison.OrdinalIgnoreCase) == true ? 0 : (x.ExpectedLitres ?? 0) - x.ClosingDip, 0)).ToList();
        reportItems.AddRange(tankRows);
        var attendantRows = sessionRows.Concat(saleRows).Where(x => x.Product.Contains(" · ", StringComparison.Ordinal)).Select(x => x with { Type = "Attendant activity" }).ToList();
        var deliveryRows = receiptRows.Concat(sessionRows.Where(x => x.Type == "Pump session")).Concat(saleRows).ToList();
        var financeRows = reportItems.Where(x => x.Type is "Pump session" or "LPG fill" or "Expense").ToList();
        var salesRows = sessionRows.Concat(saleRows).ToList();
        _reportCategoryRows["Summary"] = reportItems.OrderByDescending(x => x.Date).ThenBy(x => x.Type).ToList();
        _reportCategoryRows["Product / Delivery"] = deliveryRows.OrderByDescending(x => x.Date).ToList();
        _reportCategoryRows["Profit & Loss"] = financeRows.OrderByDescending(x => x.Date).ToList();
        _reportCategoryRows["Sales"] = salesRows.OrderByDescending(x => x.Date).ToList();
        _reportCategoryRows["Tank Stock"] = tankRows.OrderByDescending(x => x.Date).ToList();
        _reportCategoryRows["Fuel Receipts"] = receiptRows.OrderByDescending(x => x.Date).ToList();
        _reportCategoryRows["Attendants"] = attendantRows.OrderByDescending(x => x.Date).ToList();
        _reportCategoryRows["Expenses"] = expenseRows.OrderByDescending(x => x.Date).ToList();
        _reportCategoryRows["RTT"] = rttRows.OrderByDescending(x => x.Date).ToList();
        var sorted = reportItems.OrderByDescending(x => x.Date).ThenBy(x => x.Type).ToList();
        foreach (var (category, grid) in _reportTables) grid.ItemsSource = _reportCategoryRows.GetValueOrDefault(category) ?? [];
        await RefreshReportVisualsAsync(sorted);
        _overviewGrid.ItemsSource = sorted.Take(12).ToList();
        SetStatus($"Reports ready · {from:yyyy-MM-dd} to {to:yyyy-MM-dd} · {sorted.Count:N0} summary rows · exports follow the selected tab");
    }

    private async Task RefreshMetricsAsync()
    {
        if (_db is null) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var pump = await _db.PumpSessions.Where(x => x.BranchId == _branchId && x.SessionDate == today).SumAsync(x => (decimal?)x.SaleAmount) ?? 0;
        var lpg = await _db.ProductSales.Where(x => x.BranchId == _branchId && x.SaleDate == today).SumAsync(x => (decimal?)x.SaleAmount) ?? 0;
        var allExpenses = await _db.Expenses.Where(x => x.BranchId == _branchId).ToListAsync();
        var expenses = allExpenses.Where(x => x.ExpenseAt.LocalDateTime.Date == DateTime.Today).Sum(x => x.Amount);
        var shortOver = (await _db.PumpSessions.Where(x => x.BranchId == _branchId && x.SessionDate == today).SumAsync(x => (decimal?)x.ShortOver) ?? 0)
            + (await _db.ProductSales.Where(x => x.BranchId == _branchId && x.SaleDate == today).SumAsync(x => (decimal?)x.ShortOver) ?? 0);
        var grossProfit = (await _db.PumpSessions.Where(x => x.BranchId == _branchId && x.SessionDate == today).SumAsync(x => (decimal?)x.GrossProfit) ?? 0)
            + (await _db.ProductSales.Where(x => x.BranchId == _branchId && x.SaleDate == today).SumAsync(x => (decimal?)x.GrossProfit) ?? 0);
        SetMetric("MetricPumpSales", pump.ToString("C2", CultureInfo.GetCultureInfo("en-NG")));
        SetMetric("MetricLPGfills", lpg.ToString("C2", CultureInfo.GetCultureInfo("en-NG")));
        SetMetric("MetricGrossProfit", grossProfit.ToString("C2", CultureInfo.GetCultureInfo("en-NG")));
        SetMetric("MetricExpenses", expenses.ToString("C2", CultureInfo.GetCultureInfo("en-NG")));
        SetMetric("MetricShortOver", shortOver.ToString("C2", CultureInfo.GetCultureInfo("en-NG")));
    }

    private async Task RefreshAccountBalancesAsync()
    {
        if (_db is null || _accountBalanceGrid is null) return;
        var accounts = await _db.Accounts.Where(x => x.BranchId == _branchId && x.IsActive).OrderBy(x => x.AccountType).ThenBy(x => x.Name).ToListAsync();
        var txns = await _db.AccountTransactions.Where(x => x.BranchId == _branchId).ToListAsync();
        _accountBalanceGrid.ItemsSource = accounts.Select(account =>
        {
            var balance = CalculateAccountBalance(account, txns, DateOnly.FromDateTime(DateTime.Today));
            return new { account.Name, account.AccountType, account.AccountNumber, account.Provider, Balance = balance };
        }).ToList();
    }

    private static decimal CalculateAccountBalance(Account account, IReadOnlyCollection<AccountTransaction> transactions, DateOnly cutoff)
    {
        var relevant = transactions.Where(t => DateOnly.FromDateTime(t.OccurredAt.LocalDateTime) <= cutoff && (t.AccountId == account.Id || t.TargetAccountId == account.Id))
            .OrderBy(t => t.OccurredAt).ThenBy(t => t.Id).ToList();
        decimal balance = 0;
        AccountTransaction? countAnchor = null;
        if (account.AccountType == "CASH") countAnchor = relevant.Where(t => t.AccountId == account.Id && t.TransactionType == "Cash Count").LastOrDefault();
        if (countAnchor is not null) { balance = countAnchor.Amount; relevant = relevant.Where(t => t.Id != countAnchor.Id && t.OccurredAt >= countAnchor.OccurredAt && t.Id > countAnchor.Id).ToList(); }
        foreach (var t in relevant)
        {
            if (t.TransactionType == "Cash Count") continue;
            if (t.TargetAccountId == account.Id && t.AccountId != account.Id) { balance += t.Amount; continue; }
            if (t.AccountId != account.Id) continue;
            if (t.TransactionType is "Opening Balance" or "Account Receipt" or "Cash collection" or "POS Transaction" or "POS collection") balance += t.Amount;
            else if (t.TransactionType is "Account Payment / Amount Sent" or "Bank Payment / Withdrawal" or "Cash Expense" or "Account Expense" or "Expense" or "Product Payment") balance -= t.Amount;
            else if (t.TransactionType is "Account Transfer" or "Bank Deposit" or "POS Settlement")
            {
                if (t.TargetAccountId is long target && target != account.Id) balance -= t.Amount;
                else if (t.TargetAccountId is null) balance += t.Amount;
            }
        }
        return balance;
    }

    private async Task RefreshCashReconciliationAsync()
    {
        if (_db is null || _reconciliationSummary is null) return;
        var date = Date(_accountReconcileDate);
        var sessions = await _db.PumpSessions.Where(x => x.BranchId == _branchId && x.SessionDate == date).ToListAsync();
        var collections = await _db.CashCollections.Where(x => x.BranchId == _branchId).ToListAsync();
        var sessionIds = sessions.Select(x => x.Id).ToHashSet();
        var dayCollections = collections.Where(x => sessionIds.Contains(x.PumpSessionId)).ToList();
        var expectedCash = sessions.Sum(x => x.CashCollected);
        var expectedPos = sessions.Sum(x => x.PosAmount);
        var recordedCash = dayCollections.Count == 0 ? expectedCash : dayCollections.Sum(x => x.CashAmount);
        var recordedPos = dayCollections.Count == 0 ? expectedPos : dayCollections.Sum(x => x.PosAmount);
        var sessionShort = sessions.Sum(x => x.ShortOver);
        var allBranchTxns = await _db.AccountTransactions.Where(x => x.BranchId == _branchId).ToListAsync();
        var txns = allBranchTxns.Where(x => DateOnly.FromDateTime(x.OccurredAt.LocalDateTime) == date).ToList();
        var accounts = await _db.Accounts.Where(x => x.BranchId == _branchId).ToListAsync();
        var types = accounts.ToDictionary(x => x.Id, x => x.AccountType);
        var cashExpense = txns.Where(t => t.TransactionType == "Cash Expense" && t.AccountId is long id && types.GetValueOrDefault(id) == "CASH").Sum(t => t.Amount);
        var cashMoved = txns.Where(t => t.AccountId is long id && types.GetValueOrDefault(id) == "CASH" && ((t.TargetAccountId is long target && types.GetValueOrDefault(target) == "BANK" && t.TransactionType is "Bank Deposit" or "Account Transfer") || (t.TargetAccountId is null && t.TransactionType is "Account Payment / Amount Sent" or "Product Payment"))).Sum(t => t.Amount);
        var posEntered = txns.Where(t => t.AccountId is long id && types.GetValueOrDefault(id) == "POS" && t.TransactionType == "POS Transaction").Sum(t => t.Amount);
        var posMoved = txns.Where(t => t.AccountId is long id && types.GetValueOrDefault(id) == "POS" && ((t.TargetAccountId is long target && t.TransactionType is "POS Settlement" or "Account Transfer") || (t.TargetAccountId is null && t.TransactionType is "Account Payment / Amount Sent" or "Product Payment" or "Account Expense"))).Sum(t => t.Amount);
        var vaultCount = txns.Where(t => t.AccountId is long id && types.GetValueOrDefault(id) == "CASH" && t.TransactionType == "Cash Count").OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.Id).Select(t => (decimal?)t.Amount).FirstOrDefault();
        var cashAvailable = vaultCount ?? Math.Max(expectedCash - cashExpense, 0);
        var cashPending = Math.Max(cashAvailable - cashMoved, 0);
        var posPending = Math.Max((posEntered > 0 ? posEntered : expectedPos) - posMoved, 0);
        var expectedTotal = Math.Max(expectedCash - cashExpense, 0) + expectedPos;
        var sessionCollectionGap = recordedCash + recordedPos - expectedCash - expectedPos;
        var movementGap = cashMoved + posMoved - expectedTotal;
        var status = Math.Abs(sessionCollectionGap) <= 0.01m && Math.Abs(movementGap) <= 0.01m ? "BALANCED" : movementGap < -0.01m ? "PENDING / SHORT" : "OVER";
        _reconciliationSummary.Text = $"{date:yyyy-MM-dd} · Expected cash ₦{expectedCash:N2} · Expected POS ₦{expectedPos:N2} · Recorded cash/POS ₦{recordedCash + recordedPos:N2} (gap {sessionCollectionGap:+#,##0.00;-#,##0.00;0.00}) · Vault count ₦{(vaultCount?.ToString("N2") ?? "not counted")} · Cash pending ₦{cashPending:N2} · POS pending ₦{posPending:N2} · Cash expenses ₦{cashExpense:N2} · {status} · pump short / over ₦{sessionShort:+#,##0.00;-#,##0.00;0.00}";
    }

    private async Task LoadOpeningMeterAsync()
    {
        if (_db is null || SelectedId(_sessionPump) is not long id) return;
        var previous = await _db.PumpSessions.Where(x => x.PumpId == id && x.BranchId == _branchId)
            .OrderByDescending(x => x.SessionDate).ThenByDescending(x => x.Id).Select(x => (decimal?)x.ClosingMeter).FirstOrDefaultAsync();
        _openingMeter.Text = (previous ?? 0).ToString("0.##");
        var pump = await _db.Pumps.SingleOrDefaultAsync(x => x.Id == id);
        if (pump is null) return;
        var tank = await _db.Tanks.SingleAsync(x => x.Id == pump.TankId);
        var product = await _db.Products.SingleAsync(x => x.Id == tank.ProductId);
        if (!product.IsMetered)
        {
            SetStatus($"{product.Code} is set to unit-based sales. Use its dedicated sales workflow, not a pump session.");
            return;
        }
        await LoadEffectiveSessionPriceAsync();
    }

    private async Task LoadEffectiveSessionPriceAsync()
    {
        if (_db is null || SelectedId(_sessionPump) is not long pumpId) return;
        try
        {
            var sessionDate = Date(_sessionDate);
            var pump = await _db.Pumps.SingleOrDefaultAsync(x => x.Id == pumpId && x.BranchId == _branchId);
            if (pump is null) return;
            var tank = await _db.Tanks.SingleAsync(x => x.Id == pump.TankId);
            var product = await _db.Products.SingleAsync(x => x.Id == tank.ProductId);
            var price = await _db.ProductPrices.Where(x => x.ProductId == product.Id && x.EffectiveDate <= sessionDate)
                .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id).Select(x => (decimal?)x.UnitPrice).FirstOrDefaultAsync();
            _sessionPrice.Text = price?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
            _sessionPriceStatus.Text = price.HasValue ? $"Applied {product.Code} price: ₦{price:N2} per {product.UnitOfMeasure}, effective {sessionDate:yyyy-MM-dd}." : $"No {product.Code} selling price is effective on {sessionDate:yyyy-MM-dd}. Set one under Cash & Accounts → Selling prices.";
        }
        catch (Exception ex) { _sessionPrice.Text = ""; if (_sessionPriceStatus is not null) _sessionPriceStatus.Text = ex.Message; }
    }

    private async Task LoadEffectiveLpgPriceAsync()
    {
        if (_db is null || SelectedId(_lpgTank) is not long tankId) return;
        try
        {
            var saleDate = Date(_lpgDate);
            var tank = await _db.Tanks.SingleAsync(x => x.Id == tankId && x.BranchId == _branchId);
            var product = await _db.Products.SingleAsync(x => x.Id == tank.ProductId);
            var price = await _db.ProductPrices.Where(x => x.ProductId == product.Id && x.EffectiveDate <= saleDate)
                .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id).Select(x => (decimal?)x.UnitPrice).FirstOrDefaultAsync();
            _lpgPrice.Text = price?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
            _lpgPrice.ToolTip = price.HasValue ? $"Automatically applied price effective {saleDate:yyyy-MM-dd}" : $"No effective selling price for {product.Code}. Set one under Cash & Accounts.";
        }
        catch { _lpgPrice.Text = ""; }
    }

    private async Task SaveProductPriceAsync()
    {
        var db = Db(); var productId = RequiredId(_priceProduct, "product"); var unitPrice = Number(_priceAmount); var effectiveDate = Date(_priceEffectiveDate);
        if (unitPrice <= 0) throw new ArgumentException("Selling price must be greater than zero.");
        var price = await db.ProductPrices.SingleOrDefaultAsync(x => x.ProductId == productId && x.EffectiveDate == effectiveDate);
        if (price is null) db.ProductPrices.Add(new ProductPrice { ProductId = productId, UnitPrice = unitPrice, EffectiveDate = effectiveDate });
        else price.UnitPrice = unitPrice;
        await db.SaveChangesAsync(); Clear(_priceAmount);
        SetStatus("Selling price saved · future sessions and fills on or after the effective date will use it automatically.");
        await RefreshAllAsync(); await LoadEffectiveSessionPriceAsync(); await LoadEffectiveLpgPriceAsync();
    }

    private async Task LoadDipOpeningAsync()
    {
        if (_db is null || SelectedId(_dipTank) is not long tankId) return;
        var dipClosings = await _db.TankDips.Where(x => x.BranchId == _branchId && x.TankId == tankId).OrderByDescending(x => x.Id).Select(x => (decimal?)x.ClosingDip).ToListAsync();
        var previous = dipClosings.FirstOrDefault();
        _dipOpening.Text = previous?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        _dipOpening.ToolTip = previous.HasValue ? "Opening stock baseline (carried from prior closing dip)" : "Opening stock baseline (enter the measured starting stock for this first baseline)";
    }

    private async Task RefreshReportVisualsAsync(IReadOnlyCollection<ReportRow> rows)
    {
        if (_reportVisuals is null || _db is null) return;
        _reportVisuals.Children.Clear();
        var salesRows = rows.Where(x => x.Type is "Pump session" or "LPG fill" or "Attendant activity").ToList();
        var sales = salesRows.Sum(x => x.Sales);
        var cost = salesRows.Sum(x => x.CostOfGoodsSold);
        var gross = salesRows.Sum(x => x.GrossProfit);
        var expenses = -rows.Where(x => x.Type == "Expense").Sum(x => x.Sales);
        var from = Date(_reportFrom); var to = Date(_reportTo);
        var periodSessions = await _db.PumpSessions.Where(x => x.BranchId == _branchId && x.SessionDate >= from && x.SessionDate <= to).ToListAsync();
        var sessionIds = periodSessions.Select(x => x.Id).ToList();
        var negativeShorts = periodSessions.ToDictionary(x => x.Id, x => Math.Max(-x.ShortOver, 0));
        var shortAllocations = sessionIds.Count == 0 ? [] : await _db.AttendantShortAllocations.Where(x => sessionIds.Contains(x.PumpSessionId)).ToListAsync();
        var shortReconciliations = shortAllocations.Count == 0 ? [] : await _db.AttendantShortReconciliations.Where(x => shortAllocations.Select(a => a.ReconciliationId).Distinct().Contains(x.Id) && x.RecoveryDate <= to).ToListAsync();
        var applicableReconciliations = shortReconciliations.Select(x => x.Id).ToHashSet();
        var recoveredShorts = shortAllocations.Where(x => applicableReconciliations.Contains(x.ReconciliationId)).GroupBy(x => x.PumpSessionId).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
        var unrecoveredShort = negativeShorts.Sum(x => Math.Max(x.Value - recoveredShorts.GetValueOrDefault(x.Key), 0));
        var deliveryLoss = await _db.ReceiptShortSettlements.Where(x => x.BranchId == _branchId && x.SettlementDate >= from && x.SettlementDate <= to).SumAsync(x => (decimal?)x.StationBorneCost) ?? 0;
        var net = gross - expenses - unrecoveredShort - deliveryLoss;
        var financeBars = new[]
        {
            new ChartBar("Sales", sales, sales.ToString("C0", CultureInfo.GetCultureInfo("en-NG")), "#155EEF"),
            new ChartBar("COGS", cost, cost.ToString("C0", CultureInfo.GetCultureInfo("en-NG")), "#F79009"),
            new ChartBar("Gross profit", gross, gross.ToString("C0", CultureInfo.GetCultureInfo("en-NG")), "#12B76A"),
            new ChartBar("Expenses", expenses, expenses.ToString("C0", CultureInfo.GetCultureInfo("en-NG")), "#7F56D9"),
            new ChartBar("Unrecovered shorts", unrecoveredShort, unrecoveredShort.ToString("C0", CultureInfo.GetCultureInfo("en-NG")), "#F04438"),
            new ChartBar("Station-borne delivery shortage", deliveryLoss, deliveryLoss.ToString("C0", CultureInfo.GetCultureInfo("en-NG")), "#B42318"),
            new ChartBar("Net profit", net, net.ToString("C0", CultureInfo.GetCultureInfo("en-NG")), net < 0 ? "#D92D20" : "#101828")
        };
        var financeChart = Build3DBarChart("3D profit and loss · selected period", financeBars);
        var profitFormula = new TextBlock { Text = $"Formula · Sales = sold units × effective product price; COGS = FIFO delivery cost; Gross profit = sales − COGS; Net profit = gross profit − pump/LPG and operating expenses − unrecovered pump shorts − station-borne delivery shortages. Tank dip gain/loss is shown separately and excluded until reconciled. Unrecovered shorts: ₦{unrecoveredShort:N2}; station-borne delivery shortage: ₦{deliveryLoss:N2}.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(7, 0, 7, 8) };
        _reportVisuals.Children.Add(profitFormula);
        _reportVisuals.Children.Add(financeChart);
        _overviewVisuals.Children.Clear();
        _overviewVisuals.Children.Add(new TextBlock { Text = profitFormula.Text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(7, 0, 7, 8) });
        _overviewVisuals.Children.Add(Build3DBarChart("3D station performance · selected period", financeBars));
        _reportVisuals.Children.Add(Subheading("Tank gauges · latest closing dip vs capacity"));
        var tanks = await (from t in _db.Tanks join p in _db.Products on t.ProductId equals p.Id where t.BranchId == _branchId && t.IsActive orderby t.Number select new { t.Id, t.Number, Product = p.Code, Unit = p.UnitOfMeasure, t.CapacityLitres }).ToListAsync();
        var gauges = new UniformGrid { Columns = Math.Max(1, Math.Min(4, tanks.Count)) };
        var allDips = await _db.TankDips.Where(x => x.BranchId == _branchId).ToListAsync();
        foreach (var unvalued in allDips.Where(d => !d.BaselineCost.HasValue && d.Notes != null && d.Notes.Contains("opening baseline", StringComparison.OrdinalIgnoreCase)))
        {
            var tank = tanks.FirstOrDefault(t => t.Id == unvalued.TankId);
            if (tank is null || unvalued.ClosingDip <= 0) continue;
            var baselineDate = DateOnly.FromDateTime(unvalued.RecordedAt.LocalDateTime);
            var receipts = await _db.FuelReceipts.Where(r => r.BranchId == _branchId && r.ProductCodeSnapshot == tank.Product && r.ReceiptDate <= baselineDate).ToListAsync();
            var costedQuantity = receipts.Sum(r => r.ReceivedLitres);
            if (costedQuantity <= 0) continue;
            var weightedUnitCost = receipts.Sum(r => r.ReceivedLitres * (r.EffectiveUnitCost ?? r.UnitCost)) / costedQuantity;
            if (weightedUnitCost > 0) unvalued.BaselineCost = unvalued.ClosingDip * weightedUnitCost;
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
        var baselineDips = tanks.Select(t => allDips.Where(d => d.TankId == t.Id && d.Notes != null && d.Notes.Contains("opening baseline", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.RecordedAt.LocalDateTime).ThenBy(d => d.Id).FirstOrDefault()).Where(d => d is not null).ToList();
        var valuedBaselines = baselineDips.Where(d => d!.BaselineCost.HasValue).ToList();
        var baselineUnits = baselineDips.Sum(d => d!.ClosingDip);
        var baselineValue = valuedBaselines.Sum(d => d!.BaselineCost!.Value);
        var unvaluedBaselineCount = baselineDips.Count - valuedBaselines.Count;
        var baselineSummary = new TextBlock { Text = $"Opening inventory baseline · {baselineUnits:N1} product units · ₦{baselineValue:N2} cost (excluded from profit){(unvaluedBaselineCount > 0 ? $" · {unvaluedBaselineCount} baseline(s) need a costed opening receipt" : string.Empty)}", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(7, 4, 7, 8) };
        _reportVisuals.Children.Add(baselineSummary);
        _overviewVisuals.Children.Add(baselineSummary);
        var varianceBars = new List<ChartBar>();
        foreach (var t in tanks)
        {
            var tankDips = allDips.Where(x => x.TankId == t.Id).ToList();
            var dip = tankDips.OrderByDescending(x => x.RecordedAt.LocalDateTime).ThenByDescending(x => x.Id).Select(x => new { x.ClosingDip, x.RecordedAt }).FirstOrDefault();
            var current = dip?.ClosingDip ?? 0; var percent = t.CapacityLitres <= 0 ? 0 : Math.Clamp((double)(current / t.CapacityLitres), 0, 1);
            var card = new StackPanel { Margin = new Thickness(5), Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)) };
            card.Children.Add(new TextBlock { Text = $"Tank {t.Number} · {t.Product}", Margin = new Thickness(8, 6, 8, 1), FontWeight = FontWeights.SemiBold });
            card.Children.Add(new TextBlock { Text = dip is null ? "No closing dip recorded" : $"{current:N1} / {t.CapacityLitres:N1} {t.Unit} · {percent:P0} full · {dip.RecordedAt:yyyy-MM-dd}", Margin = new Thickness(8, 0, 8, 3), FontSize = 12, Foreground = (Brush)FindResource("Muted") });
            var baseline = baselineDips.FirstOrDefault(d => d!.TankId == t.Id);
            card.Children.Add(new TextBlock { Text = baseline is null ? "Opening baseline not recorded" : baseline.BaselineCost.HasValue ? $"Baseline · {baseline.ClosingDip:N1} {t.Unit} · ₦{baseline.BaselineCost.Value:N2} cost" : $"Baseline · {baseline.ClosingDip:N1} {t.Unit} · cost not available", Margin = new Thickness(8, 0, 8, 3), FontSize = 11, Foreground = (Brush)FindResource("Muted") });
            card.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = percent, Height = 13, Margin = new Thickness(8, 2, 8, 8), Foreground = new SolidColorBrush(Color.FromRgb(21, 94, 239)) }); gauges.Children.Add(card);
            var tankVariance = tankDips.Where(x => DateOnly.FromDateTime(x.RecordedAt.LocalDateTime) >= from && DateOnly.FromDateTime(x.RecordedAt.LocalDateTime) <= to && x.ExpectedLitres.HasValue && x.Notes != "Opening baseline").Sum(x => x.ExpectedLitres!.Value - x.ClosingDip);
            varianceBars.Add(new ChartBar($"Tank {t.Number}", tankVariance, $"{tankVariance:+0.0;-0.0;0.0} {t.Unit} {(tankVariance > 0 ? "loss" : tankVariance < 0 ? "gain" : "match")}", tankVariance > 0 ? "#D92D20" : "#12B76A"));
        }
        if (tanks.Count == 0) _reportVisuals.Children.Add(new TextBlock { Text = "Create a tank and record an opening baseline to see its gauge." });
        else
        {
            _reportVisuals.Children.Add(gauges);
            _reportVisuals.Children.Add(Build3DBarChart("3D tank variance · expected closing − physical closing", varianceBars));
            _overviewVisuals.Children.Add(Build3DBarChart("3D tank gain / loss · expected closing − physical closing", varianceBars));
        }
    }

    private static StackPanel Build3DBarChart(string title, IReadOnlyList<ChartBar> bars)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 8) };
        panel.Children.Add(Subheading(title));
        if (bars.Count == 0) { panel.Children.Add(new TextBlock { Text = "No measurements for this period.", Margin = new Thickness(7), Foreground = Brushes.Gray }); return panel; }
        const double width = 940, height = 270, baseline = 205, maxBar = 142, depth = 17, depthRise = 11;
        var max = Math.Max(1m, bars.Max(x => Math.Abs(x.Value)));
        var canvas = new Canvas { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)), ClipToBounds = true };
        canvas.Children.Add(new Line { X1 = 28, Y1 = baseline, X2 = width - 18, Y2 = baseline, Stroke = new SolidColorBrush(Color.FromRgb(152, 162, 179)), StrokeThickness = 1 });
        var slot = (width - 60) / bars.Count; var barWidth = Math.Min(76, slot * 0.48);
        for (var i = 0; i < bars.Count; i++)
        {
            var item = bars[i]; var barHeight = Math.Max(2, maxBar * (double)(Math.Abs(item.Value) / max));
            var x = 34 + i * slot + (slot - barWidth - depth) / 2; var y = baseline - barHeight;
            var baseColor = (Color)ColorConverter.ConvertFromString(item.Color);
            var front = new PointCollection { new(x, y), new(x + barWidth, y), new(x + barWidth, baseline), new(x, baseline) };
            var side = new PointCollection { new(x + barWidth, y), new(x + barWidth + depth, y - depthRise), new(x + barWidth + depth, baseline - depthRise), new(x + barWidth, baseline) };
            var top = new PointCollection { new(x, y), new(x + depth, y - depthRise), new(x + barWidth + depth, y - depthRise), new(x + barWidth, y) };
            canvas.Children.Add(new Polygon { Points = side, Fill = new SolidColorBrush(Shade(baseColor, 0.68)) });
            canvas.Children.Add(new Polygon { Points = front, Fill = new SolidColorBrush(baseColor) });
            canvas.Children.Add(new Polygon { Points = top, Fill = new SolidColorBrush(Shade(baseColor, 1.23)) });
            var value = new TextBlock { Text = item.Display, Width = slot, TextAlignment = TextAlignment.Center, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)) };
            Canvas.SetLeft(value, 22 + i * slot); Canvas.SetTop(value, Math.Max(2, y - 24)); canvas.Children.Add(value);
            var label = new TextBlock { Text = item.Label, Width = slot, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)) };
            Canvas.SetLeft(label, 22 + i * slot); Canvas.SetTop(label, baseline + 6); canvas.Children.Add(label);
        }
        panel.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(234, 236, 240)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = canvas });
        return panel;
    }

    private static Color Shade(Color color, double factor) => Color.FromRgb((byte)Math.Clamp(color.R * factor, 0, 255), (byte)Math.Clamp(color.G * factor, 0, 255), (byte)Math.Clamp(color.B * factor, 0, 255));

    private async Task PostPumpSessionAsync()
    {
        var db = Db();
        var id = RequiredId(_sessionPump, "pump");
        var pump = await db.Pumps.SingleAsync(x => x.Id == id && x.BranchId == _branchId);
        var tank = await db.Tanks.SingleAsync(x => x.Id == pump.TankId);
        var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId);
        if (!product.IsMetered) throw new InvalidOperationException($"{product.Code} is a weigh / package product. Use LPG Fills.");
        var run = await db.PumpRuns.Where(x => x.BranchId == _branchId && x.PumpId == pump.Id && x.Status == "Open").OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (run is null)
        {
            run = new PumpRun { BranchId = _branchId, PumpId = pump.Id, TankId = tank.Id, ProductCodeSnapshot = product.Code, OpeningMeter = Number(_openingMeter), StartedAt = DateTimeOffset.Now };
            db.PumpRuns.Add(run); await db.SaveChangesAsync();
        }
        if (run.TankId != tank.Id || !run.ProductCodeSnapshot.Equals(product.Code, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The pump has an open run for a different tank product. Finish that product before posting this session.");
        var session = new PumpSession { BranchId = _branchId, PumpId = pump.Id, RunId = run.Id, AttendantId = SelectedId(_sessionStaff),
            ProductCodeSnapshot = product.Code, UnitOfMeasureSnapshot = product.UnitOfMeasure, OpeningMeter = Number(_openingMeter), ClosingMeter = Number(_sessionClosing),
            PricePerLitre = Number(_sessionPrice), CashCollected = Number(_sessionCash), PosAmount = Number(_sessionPos),
            Expenses = Number(_sessionExpense), RttLitres = Number(_sessionRtt), SessionDate = Date(_sessionDate) };
        await new FifoInventoryService(db).PostPumpSessionAsync(session);
        run.ClosingMeter = session.ClosingMeter;
        Clear(_sessionClosing, _sessionCash, _sessionPos, _sessionExpense, _sessionRtt); _sessionDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
        SetStatus($"Session posted · {session.LitresSold:N2} {product.UnitOfMeasure} sold · cost of fuel sold ₦{session.CostOfGoodsSold:N2} · gross profit ₦{session.GrossProfit:N2} · short/over ₦{session.ShortOver:N2}");
        await RefreshAllAsync();
    }

    private async Task SaveReceiptAsync()
    {
        var db = Db();
        var tank = await db.Tanks.SingleAsync(x => x.Id == RequiredId(_receiptTank, "tank") && x.BranchId == _branchId);
        var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId);
        var invoice = Number(_invoiceQty); var received = Number(_receivedQty); var cost = Number(_receiptCost);
        if (invoice <= 0 || received <= 0 || cost <= 0) throw new ArgumentException("Invoice quantity, received quantity and unit cost must be greater than zero.");
        var receiptType = _receiptType.SelectedItem?.ToString() ?? "Delivery";
        if (receiptType == "Delivery" && (string.IsNullOrWhiteSpace(_supplier.Text) || string.IsNullOrWhiteSpace(_invoice.Text) ||
            string.IsNullOrWhiteSpace(_vehicle.Text) || string.IsNullOrWhiteSpace(_driver.Text) || string.IsNullOrWhiteSpace(_driverPhone.Text)))
            throw new ArgumentException("For deliveries, enter supplier, invoice number, delivery vehicle, driver and driver phone. Opening balances do not need these details.");
        var current = await db.FuelReceipts.Where(x => x.BranchId == _branchId && x.TankId == tank.Id).SumAsync(x => (decimal?)x.RemainingLitres) ?? 0;
        if (current + received > tank.CapacityLitres) throw new ArgumentException($"Delivery exceeds tank capacity. Current book stock {current:N2} {product.UnitOfMeasure}; capacity {tank.CapacityLitres:N2}.");
        var batch = new FuelReceipt { BranchId = _branchId, TankId = tank.Id, ProductCodeSnapshot = product.Code, SupplierNameSnapshot = _supplier.Text.Trim(),
            UnitOfMeasureSnapshot = product.UnitOfMeasure, InvoiceNumber = _invoice.Text.Trim(), DeliveryNote = NullIfEmpty(_deliveryNote.Text),
            VehicleNumber = _vehicle.Text.Trim(), DriverName = _driver.Text.Trim(), DriverPhone = _driverPhone.Text.Trim(), InvoiceLitres = invoice, ReceivedLitres = received, RemainingLitres = received,
            UnitCost = cost, EffectiveUnitCost = cost, TotalCost = received * cost, ShortOverLitres = received - invoice,
            VarianceValue = (received - invoice) * cost, ReceiptDate = Date(_receiptDate), ReceiptType = receiptType };
        db.FuelReceipts.Add(batch); await db.SaveChangesAsync();
        Clear(_supplier, _invoice, _deliveryNote, _vehicle, _driver, _driverPhone, _invoiceQty, _receivedQty, _receiptCost); _receiptDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
        var shortage = Math.Max(invoice - received, 0);
        SetStatus(receiptType == "Opening Balance"
            ? $"Opening stock saved · {received:N2} {product.UnitOfMeasure} added to {product.Code} FIFO stock."
            : $"Delivery saved · {received:N2} {product.UnitOfMeasure} added to {product.Code} FIFO stock · invoice shortage {shortage:N2} {product.UnitOfMeasure} · overage {Math.Max(received - invoice, 0):N2}");
        await RefreshAllAsync();
    }

    private async Task PostLpgSaleAsync()
    {
        var db = Db(); var tank = await db.Tanks.SingleAsync(x => x.Id == RequiredId(_lpgTank, "LPG tank") && x.BranchId == _branchId);
        var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId);
        if (product.Code != "LPG") throw new ArgumentException("Choose an LPG tank.");
        var quantity = Number(_lpgQty); var price = Number(_lpgPrice); var cash = Number(_lpgCash); var pos = Number(_lpgPos); var expense = Number(_lpgExpenses);
        if (quantity <= 0 || price <= 0) throw new ArgumentException("LPG filled quantity and price per kg must be greater than zero.");
        var cylinderCount = string.IsNullOrWhiteSpace(_cylinderCount.Text) ? (int?)null : int.Parse(_cylinderCount.Text, CultureInfo.InvariantCulture);
        if (cylinderCount < 0) throw new ArgumentException("Cylinder count cannot be negative.");
        var saleDate = Date(_lpgDate);
        var batches = await db.FuelReceipts.Where(x => x.BranchId == _branchId && x.TankId == tank.Id && x.ReceiptDate <= saleDate && x.RemainingLitres > 0)
            .OrderBy(x => x.ReceiptDate).ThenBy(x => x.Id).ToListAsync();
        if (batches.Sum(x => x.RemainingLitres) < quantity) throw new InvalidOperationException($"Not enough LPG stock. Available {batches.Sum(x => x.RemainingLitres):N2} kg; requested {quantity:N2} kg.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var sale = new ProductSale { BranchId = _branchId, TankId = tank.Id, AttendantId = SelectedId(_lpgStaff), ProductCodeSnapshot = "LPG",
            UnitOfMeasureSnapshot = "kg", Quantity = quantity, UnitPrice = price, SaleAmount = quantity * price, CashCollected = cash, PosAmount = pos, Expenses = expense,
            ShortOver = cash + pos - (quantity * price - expense), CylinderCount = cylinderCount, CylinderSize = NullIfEmpty(_cylinderSize.Text), SaleDate = saleDate };
        db.ProductSales.Add(sale); await db.SaveChangesAsync();
        var left = quantity; decimal cogs = 0;
        foreach (var batch in batches)
        {
            if (left <= 0) break;
            var used = Math.Min(left, batch.RemainingLitres); var unitCost = batch.EffectiveUnitCost ?? batch.UnitCost;
            batch.RemainingLitres -= used; left -= used; cogs += used * unitCost;
            db.ProductSaleAllocations.Add(new ProductSaleAllocation { ProductSaleId = sale.Id, FuelReceiptId = batch.Id, Quantity = used, UnitCost = unitCost });
        }
        sale.CostOfGoodsSold = cogs; sale.GrossProfit = sale.SaleAmount - cogs;
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        Clear(_lpgQty, _lpgCash, _lpgPos, _lpgExpenses, _cylinderCount, _cylinderSize); _lpgDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
        SetStatus($"LPG fill posted · {quantity:N2} kg · cost of LPG sold ₦{cogs:N2} · gross profit ₦{sale.GrossProfit:N2} · short/over ₦{sale.ShortOver:N2}");
        await RefreshAllAsync();
    }

    private async Task SaveDipAsync()
    {
        var db = Db(); var tank = await db.Tanks.SingleAsync(x => x.Id == RequiredId(_dipTank, "tank") && x.BranchId == _branchId);
        var opening = Number(_dipOpening); var physical = Number(_dipActual); var dipDate = Date(_dipDate);
        if (physical > tank.CapacityLitres || opening > tank.CapacityLitres) throw new ArgumentException("Opening and closing stock must be within tank capacity.");
        var dipHistory = await db.TankDips.Where(x => x.BranchId == _branchId && x.TankId == tank.Id).ToListAsync();
        var previous = dipHistory.Where(x => DateOnly.FromDateTime(x.RecordedAt.LocalDateTime) < dipDate || DateOnly.FromDateTime(x.RecordedAt.LocalDateTime) == dipDate)
            .OrderByDescending(x => x.RecordedAt.LocalDateTime).ThenByDescending(x => x.Id).FirstOrDefault();
        var isBaseline = previous is null;
        decimal expected = physical;
        decimal? baselineCost = null;
        if (isBaseline && physical > 0)
        {
            var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId);
            var receipts = await db.FuelReceipts.Where(x => x.BranchId == _branchId && x.ProductCodeSnapshot == product.Code && x.ReceiptDate <= dipDate).ToListAsync();
            var costedQuantity = receipts.Sum(x => x.ReceivedLitres);
            if (costedQuantity <= 0) throw new InvalidOperationException("Record a costed opening receipt before saving a non-zero tank baseline so the baseline inventory can be valued.");
            var receiptValue = receipts.Sum(x => x.ReceivedLitres * (x.EffectiveUnitCost ?? x.UnitCost));
            var weightedUnitCost = receiptValue / costedQuantity;
            if (weightedUnitCost <= 0) throw new InvalidOperationException("The tank has no positive weighted receipt cost to value this baseline.");
            baselineCost = physical * weightedUnitCost;
        }
        if (!isBaseline)
        {
            var startDate = DateOnly.FromDateTime(previous!.RecordedAt.LocalDateTime);
            var delivered = await db.FuelReceipts.Where(x => x.BranchId == _branchId && x.TankId == tank.Id && x.ReceiptDate > startDate && x.ReceiptDate <= dipDate).SumAsync(x => (decimal?)x.ReceivedLitres) ?? 0;
            var pumpIds = await db.Pumps.Where(x => x.BranchId == _branchId && x.TankId == tank.Id).Select(x => x.Id).ToListAsync();
            var pumped = await db.PumpSessions.Where(x => x.BranchId == _branchId && pumpIds.Contains(x.PumpId) && x.SessionDate > startDate && x.SessionDate <= dipDate).SumAsync(x => (decimal?)x.LitresSold) ?? 0;
            var otherSold = await db.ProductSales.Where(x => x.BranchId == _branchId && x.TankId == tank.Id && x.SaleDate > startDate && x.SaleDate <= dipDate).SumAsync(x => (decimal?)x.Quantity) ?? 0;
            expected = opening + delivered - pumped - otherSold;
        }
        var variance = isBaseline ? 0 : expected - physical;
        db.TankDips.Add(new TankDip { BranchId = _branchId, TankId = tank.Id, OpeningDip = opening, ClosingDip = physical,
            ExpectedLitres = expected, BaselineCost = baselineCost, RecordedAt = new DateTimeOffset(dipDate.ToDateTime(TimeOnly.MinValue), DateTimeOffset.Now.Offset), Notes = isBaseline ? "Opening baseline" : $"Variance {variance:+0.##;-0.##;0}" });
        var varianceLabel = variance > 0 ? "loss" : variance < 0 ? "gain" : "match";
        await db.SaveChangesAsync(); Clear(_dipOpening, _dipActual); SetStatus(isBaseline ? $"Opening baseline saved · {physical:N2} stock on {tank.Number} · baseline cost ₦{baselineCost.GetValueOrDefault():N2} (excluded from profit)." : $"Stock check saved · opening {opening:N2}; expected closing {expected:N2}; physical closing {physical:N2}; tank {varianceLabel} {variance:+0.##;-0.##;0}.");
        await RefreshAllAsync();
    }

    private async Task SaveReceiptShortReconciliationAsync()
    {
        var db = Db(); var receiptId = RequiredId(_shortReceipt, "fuel receipt");
        var receipt = await db.FuelReceipts.SingleAsync(x => x.Id == receiptId && x.BranchId == _branchId);
        var shortageLitres = Math.Max(receipt.InvoiceLitres - receipt.ReceivedLitres, 0);
        if (shortageLitres <= 0) throw new InvalidOperationException("This delivery has no invoice shortage to reconcile.");
        if (receipt.ReceivedLitres <= 0) throw new InvalidOperationException("This delivery has no received quantity to assign an effective unit cost.");
        var prior = await db.ReceiptShortSettlements.Where(x => x.BranchId == _branchId && x.FuelReceiptId == receipt.Id).ToListAsync();
        var remainingLitres = Math.Max(shortageLitres - prior.Sum(x => x.ReconciledLitres + x.StationBorneLitres), 0);
        var initialCost = shortageLitres * receipt.UnitCost;
        var remainingCost = Math.Max(initialCost - prior.Sum(x => x.ReconciledCost + x.StationBorneCost), 0);
        var reconciledLitres = Number(_shortReconciledLitres); var reconciledCost = Number(_shortReconciledCost);
        var stationLitres = Number(_shortStationLitres); var stationCost = Number(_shortStationCost);
        if (reconciledLitres + stationLitres <= 0 && reconciledCost + stationCost <= 0) throw new ArgumentException("Enter a settled or station-borne litre or cost amount.");
        if (reconciledLitres + stationLitres > remainingLitres + 0.005m || reconciledCost + stationCost > remainingCost + 0.005m)
            throw new ArgumentException($"This exceeds the outstanding shortage ({remainingLitres:N2} units / ₦{remainingCost:N2}).");
        var partyType = _shortPartyType.SelectedItem?.ToString() ?? "";
        if (partyType is not ("Supplier" or "Driver") || string.IsNullOrWhiteSpace(_shortPartyName.Text)) throw new ArgumentException("Choose Supplier or Driver and enter their name.");
        db.ReceiptShortSettlements.Add(new ReceiptShortSettlement { BranchId = _branchId, FuelReceiptId = receipt.Id, PartyType = partyType, PartyName = _shortPartyName.Text.Trim(),
            ReconciledLitres = reconciledLitres, ReconciledCost = reconciledCost, StationBorneLitres = stationLitres, StationBorneCost = stationCost,
            Disputed = _shortDisputed.IsChecked == true, SettlementDate = Date(_shortSettlementDate) });
        var totalStationCost = prior.Sum(x => x.StationBorneCost) + stationCost;
        receipt.EffectiveUnitCost = Math.Max(receipt.TotalCost + totalStationCost, 0) / receipt.ReceivedLitres;
        await db.SaveChangesAsync();
        Clear(_shortPartyName, _shortReconciledLitres, _shortReconciledCost, _shortStationLitres, _shortStationCost); _shortDisputed.IsChecked = false;
        SetStatus($"Delivery shortage reconciled · remaining {Math.Max(remainingLitres - reconciledLitres - stationLitres, 0):N2} units / ₦{Math.Max(remainingCost - reconciledCost - stationCost, 0):N2}."); await RefreshAllAsync();
    }

    private async Task FinishTankProductAsync()
    {
        var db = Db(); var tankId = RequiredId(_dipTank, "tank");
        var tank = await db.Tanks.SingleAsync(x => x.Id == tankId && x.BranchId == _branchId);
        var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId);
        var runs = await db.PumpRuns.Where(x => x.BranchId == _branchId && x.TankId == tankId && x.Status == "Open").ToListAsync();
        if (runs.Count == 0) throw new InvalidOperationException("This tank has no open pump runs to finish.");
        if (runs.Any(x => !x.ProductCodeSnapshot.Equals(product.Code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An open pump run has a different product. Resolve the tank assignment before finishing.");
        var runIds = runs.Select(x => x.Id).ToList();
        var sessionDates = await db.PumpSessions.Where(x => x.BranchId == _branchId && x.RunId != null && runIds.Contains(x.RunId.Value)).Select(x => x.SessionDate).ToListAsync();
        DateOnly? latestSessionDate = sessionDates.Count == 0 ? null : sessionDates.Max();
        var tankDips = await db.TankDips.Where(x => x.BranchId == _branchId && x.TankId == tankId).ToListAsync();
        var closingDip = tankDips.OrderByDescending(x => x.RecordedAt.LocalDateTime).ThenByDescending(x => x.Id).FirstOrDefault();
        if (closingDip is null || (latestSessionDate.HasValue && DateOnly.FromDateTime(closingDip.RecordedAt.LocalDateTime) < latestSessionDate.Value))
            throw new InvalidOperationException("Record the tank’s final closing dip after the latest pump session, then choose Product Finished again.");
        var answer = MessageBox.Show($"Finish the product in tank {tank.Number}? This closes {runs.Count} open pump run(s) and carries forward the last meter readings.", "Product Finished", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        foreach (var run in runs)
        {
            var last = await db.PumpSessions.Where(x => x.BranchId == _branchId && x.RunId == run.Id).OrderByDescending(x => x.SessionDate).ThenByDescending(x => x.Id).Select(x => (decimal?)x.ClosingMeter).FirstOrDefaultAsync();
            run.ClosingMeter = last ?? run.OpeningMeter;
            if (run.ClosingMeter is null) throw new InvalidOperationException("A pump has no saved meter reading. Record a pump session before finishing this tank product.");
            run.Status = "Closed"; run.FinishedAt = DateTimeOffset.Now;
        }
        await db.SaveChangesAsync(); SetStatus($"Product finished · {runs.Count} pump run(s) closed; meter readings carried forward.");
        await SendFinishedTankReportEmailAsync(tank, product, runs.Select(x => x.Id).ToList());
        await RefreshAllAsync();
    }

    private static string ReportDocumentsDirectory
    {
        get
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelStationERP", "ReportDocuments");
            Directory.CreateDirectory(folder); return folder;
        }
    }

    private static string ReportEmailSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelStationERP", "report-email-settings.json");

    private void LoadReportEmailSettings()
    {
        try
        {
            if (File.Exists(ReportEmailSettingsPath))
            {
                var saved = JsonSerializer.Deserialize<ReportEmailSettings>(File.ReadAllText(ReportEmailSettingsPath));
                if (saved is not null) { _reportEmailSender.Text = saved.Sender; _reportEmailRecipient.Text = saved.Recipient; _reportEmailEnabled.IsChecked = saved.Enabled; }
            }
            _reportEmailStatus.Text = _reportEmailEnabled.IsChecked == true ? "Email is enabled. SMTP credentials must be supplied using CLEON_SMTP_USERNAME and CLEON_SMTP_PASSWORD." : "Email disabled. Enable and save to send a product/delivery PDF when a tank is finished.";
        }
        catch (Exception ex) { _reportEmailStatus.Text = $"Email settings could not be read: {ex.Message}"; }
    }

    private async Task SaveReportEmailSettingsAsync()
    {
        var sender = _reportEmailSender.Text.Trim(); var recipient = _reportEmailRecipient.Text.Trim();
        try { _ = new MailAddress(sender); _ = new MailAddress(recipient); }
        catch (FormatException) { throw new ArgumentException("Enter valid sender and recipient email addresses."); }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportEmailSettingsPath)!);
        await File.WriteAllTextAsync(ReportEmailSettingsPath, JsonSerializer.Serialize(new ReportEmailSettings(sender, recipient, _reportEmailEnabled.IsChecked == true), new JsonSerializerOptions { WriteIndented = true }));
        _reportEmailStatus.Text = _reportEmailEnabled.IsChecked == true ? "Settings saved. A PDF will be emailed when Product Finished is selected; SMTP credentials are read from CLEON_SMTP_* environment variables." : "Settings saved. Product-finished email is disabled.";
        SetStatus("Product-finished email settings saved.");
    }

    private async Task SendFinishedTankReportEmailAsync(Tank tank, FuelProduct product, IReadOnlyCollection<long> runIds)
    {
        ReportEmailSettings? settings = null;
        try { if (File.Exists(ReportEmailSettingsPath)) settings = JsonSerializer.Deserialize<ReportEmailSettings>(await File.ReadAllTextAsync(ReportEmailSettingsPath)); }
        catch (Exception ex) { _reportEmailStatus.Text = $"Product is finished; email settings could not be read: {ex.Message}"; return; }
        if (settings?.Enabled != true) return;
        try
        {
            var host = Environment.GetEnvironmentVariable("CLEON_SMTP_HOST")?.Trim();
            if (string.IsNullOrWhiteSpace(host)) host = "smtp.gmail.com";
            var portText = Environment.GetEnvironmentVariable("CLEON_SMTP_PORT");
            var port = string.IsNullOrWhiteSpace(portText) ? 587 : int.Parse(portText, CultureInfo.InvariantCulture);
            if (port is < 1 or > 65535) throw new InvalidOperationException("CLEON_SMTP_PORT must be between 1 and 65535.");
            var username = Environment.GetEnvironmentVariable("CLEON_SMTP_USERNAME")?.Trim();
            var password = Environment.GetEnvironmentVariable("CLEON_SMTP_PASSWORD");
            if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrEmpty(password)) throw new InvalidOperationException("CLEON_SMTP_PASSWORD is required when CLEON_SMTP_USERNAME is set.");
            var sessions = await Db().PumpSessions.Where(x => x.BranchId == _branchId && x.RunId != null && runIds.Contains(x.RunId.Value)).ToListAsync();
            var dates = sessions.Select(x => x.SessionDate).ToList();
            var from = dates.Count == 0 ? DateOnly.FromDateTime(DateTime.Today) : dates.Min();
            var to = dates.Count == 0 ? DateOnly.FromDateTime(DateTime.Today) : dates.Max();
            var rows = sessions.Select(x => new ReportRow("Pump session", x.SessionDate, product.Code, x.LitresSold, product.UnitOfMeasure, x.SaleAmount, x.CostOfGoodsSold, x.GrossProfit, x.ShortOver)).ToList();
            var receipts = await Db().FuelReceipts.Where(x => x.BranchId == _branchId && x.TankId == tank.Id && x.ReceiptDate >= from && x.ReceiptDate <= to).ToListAsync();
            rows.AddRange(receipts.Select(x => new ReportRow("Fuel delivery", x.ReceiptDate, $"{product.Code} · {x.SupplierNameSnapshot ?? "Supplier not set"} · Invoice {x.InvoiceNumber ?? "—"}", x.ReceivedLitres, x.UnitOfMeasureSnapshot, x.TotalCost, 0, 0, Math.Max(x.InvoiceLitres - x.ReceivedLitres, 0))));
            var dip = await Db().TankDips.Where(x => x.BranchId == _branchId && x.TankId == tank.Id).OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
            if (dip is not null) rows.Add(new ReportRow("Closing tank stock", DateOnly.FromDateTime(dip.RecordedAt.LocalDateTime), $"Tank {tank.Number} · product finished", dip.ClosingDip, product.UnitOfMeasure, dip.BaselineCost ?? 0, dip.ExpectedLitres ?? 0, dip.ExpectedLitres.GetValueOrDefault() - dip.ClosingDip, 0));
            var filePath = Path.Combine(Path.GetTempPath(), $"CLEON-product-finished-{tank.Number}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            WritePdfReport(filePath, rows, from, to, BranchName);
            await ArchiveReportDocumentAsync(filePath);
            using var message = new MailMessage(settings.Sender, settings.Recipient)
            {
                Subject = $"CLEON product finished · {product.Code} · Tank {tank.Number}",
                Body = $"Product {product.Code} in Tank {tank.Number} has been finished and {runIds.Count} pump run(s) closed. Attached is the product/delivery report covering {from:yyyy-MM-dd} through {to:yyyy-MM-dd}."
            };
            message.Attachments.Add(new Attachment(filePath));
            using var client = new SmtpClient(host, port) { EnableSsl = !string.Equals(Environment.GetEnvironmentVariable("CLEON_SMTP_USE_SSL"), "false", StringComparison.OrdinalIgnoreCase), Timeout = 30000 };
            if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new System.Net.NetworkCredential(username, password);
            else client.UseDefaultCredentials = true;
            await client.SendMailAsync(message);
            _reportEmailStatus.Text = $"Sent finished-product report to {settings.Recipient}.";
            SetStatus($"Product finished · report emailed to {settings.Recipient}.");
            try { File.Delete(filePath); } catch (IOException) { }
        }
        catch (Exception ex)
        {
            _reportEmailStatus.Text = $"Product is finished, but the report email failed: {ex.GetBaseException().Message}";
            SetStatus("Product finished; report email failed. Check Report Center email settings and SMTP configuration.");
        }
    }

    private async Task AddReportDocumentAsync()
    {
        var dialog = new OpenFileDialog { Title = "Add a report document", Filter = "Documents (*.pdf;*.png;*.jpg;*.docx;*.xlsx;*.pptx)|*.pdf;*.png;*.jpg;*.jpeg;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var info = new FileInfo(dialog.FileName); if (info.Length > 10 * 1024 * 1024) throw new InvalidOperationException("Report documents must be 10 MB or smaller.");
        var destination = Path.Combine(ReportDocumentsDirectory, Path.GetFileName(dialog.FileName));
        if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) File.Copy(dialog.FileName, destination, true);
        await RefreshReportDocumentsAsync(); SetStatus($"Report document added · {Path.GetFileName(destination)}.");
    }

    private async Task OpenReportDocumentAsync()
    {
        if (_reportDocumentsList.SelectedItem is not ReportDocument document) throw new InvalidOperationException("Select a document first.");
        await Task.Run(() => Process.Start(new ProcessStartInfo(document.Path) { UseShellExecute = true }));
    }

    private async Task DownloadReportDocumentAsync()
    {
        if (_reportDocumentsList.SelectedItem is not ReportDocument document) throw new InvalidOperationException("Select a document first.");
        var dialog = new SaveFileDialog { Title = "Download report document", FileName = document.Name, Filter = "All files (*.*)|*.*", AddExtension = false };
        if (dialog.ShowDialog(this) == true) { await Task.Run(() => File.Copy(document.Path, dialog.FileName, true)); SetStatus($"Document copied to {dialog.FileName}."); }
    }

    private async Task OpenReportDocumentsFolderAsync() => await Task.Run(() => Process.Start(new ProcessStartInfo(ReportDocumentsDirectory) { UseShellExecute = true }));

    private async Task RefreshReportDocumentsAsync()
    {
        if (_reportDocumentsList is null) return;
        var files = await Task.Run(() => Directory.GetFiles(ReportDocumentsDirectory).Select(path => new ReportDocument(Path.GetFileName(path), path, new FileInfo(path).Length, File.GetLastWriteTime(path))).ToList());
        _reportDocumentsList.ItemsSource = files; _reportDocumentsList.DisplayMemberPath = nameof(ReportDocument.Name);
    }

    private static Task ArchiveReportDocumentAsync(string source)
    {
        var target = Path.Combine(ReportDocumentsDirectory, Path.GetFileName(source));
        File.Copy(source, target, true); return Task.CompletedTask;
    }

    private sealed record ReportEmailSettings(string Sender, string Recipient, bool Enabled);
    private sealed record ReportDocument(string Name, string Path, long Bytes, DateTime Modified);

    private async Task SaveExpenseAsync()
    {
        var db = Db(); var amount = Number(_expenseAmount); if (amount <= 0) throw new ArgumentException("Expense amount must be greater than zero.");
        var accountId = SelectedId(_expenseAccount);
        db.Expenses.Add(new Expense { BranchId = _branchId, Description = _expenseDescription.Text.Trim(), Category = NullIfEmpty(_expenseCategory.Text),
            Amount = amount, ExpenseAt = new DateTimeOffset(Date(_expenseDate).ToDateTime(TimeOnly.MinValue), DateTimeOffset.Now.Offset), SourceAccountId = accountId });
        if (accountId is long id) db.AccountTransactions.Add(new AccountTransaction { BranchId = _branchId, AccountId = id, TransactionType = await db.Accounts.Where(x => x.Id == id).Select(x => x.AccountType).SingleAsync() == "CASH" ? "Cash Expense" : "Account Expense", Amount = amount,
            Description = _expenseDescription.Text.Trim(), OccurredAt = new DateTimeOffset(Date(_expenseDate).ToDateTime(TimeOnly.MinValue), DateTimeOffset.Now.Offset) });
        await db.SaveChangesAsync(); Clear(_expenseDescription, _expenseCategory, _expenseAmount); SetStatus("Expense recorded."); await RefreshAllAsync();
    }

    private async Task SaveCashCollectionAsync()
    {
        var db = Db(); var sessionId = RequiredId(_cashSession, "pump session"); var cash = Number(_cashCollected); var pos = Number(_cashPos);
        if (cash < 0 || pos < 0 || cash + pos <= 0) throw new ArgumentException("Enter a positive cash or POS collection.");
        db.CashCollections.Add(new CashCollection { BranchId = _branchId, PumpSessionId = sessionId, CashAmount = cash, PosAmount = pos, CollectedAt = DateTimeOffset.Now });
        var cashAccount = await db.Accounts.Where(x => x.BranchId == _branchId && x.IsActive && x.AccountType == "CASH").OrderBy(x => x.Id).FirstOrDefaultAsync();
        var posAccount = await db.Accounts.Where(x => x.BranchId == _branchId && x.IsActive && x.AccountType == "POS").OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (cash > 0 && cashAccount is not null) db.AccountTransactions.Add(new AccountTransaction { BranchId = _branchId, AccountId = cashAccount.Id, TransactionType = "Account Receipt", Amount = cash,
            Description = $"Pump session #{sessionId} cash collection", OccurredAt = DateTimeOffset.Now });
        if (pos > 0 && posAccount is not null) db.AccountTransactions.Add(new AccountTransaction { BranchId = _branchId, AccountId = posAccount.Id, TransactionType = "POS Transaction", Amount = pos,
            Description = $"Pump session #{sessionId} POS collection", OccurredAt = DateTimeOffset.Now });
        await db.SaveChangesAsync(); Clear(_cashCollected, _cashPos); SetStatus("Cash / POS collection recorded against the selected session."); await RefreshAllAsync();
    }

    private async Task SaveAccountTransactionAsync()
    {
        var db = Db(); var amount = Number(_transactionAmount); var kind = _transactionType.SelectedItem?.ToString() ?? "";
        if (amount < 0 || (amount == 0 && kind != "Cash Count")) throw new ArgumentException("Enter an amount greater than zero. A physical cash count may be zero.");
        var accountId = RequiredId(_transactionAccount, "account");
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId && x.BranchId == _branchId && x.IsActive);
        var targetId = SelectedId(_transactionTarget);
        var receiptId = SelectedId(_transactionReceipt);
        if (string.IsNullOrWhiteSpace(_transactionReference.Text) && kind is "Account Payment / Amount Sent" or "Product Payment" or "Account Expense") throw new ArgumentException("Enter a beneficiary or payment reference for this account outflow.");
        if (kind == "Opening Balance" && await db.AccountTransactions.AnyAsync(x => x.BranchId == _branchId && x.AccountId == accountId && x.TransactionType == kind)) throw new ArgumentException("This account already has an opening balance.");
        if (kind is "Cash Count" or "Cash Expense" && account.AccountType != "CASH") throw new ArgumentException("Select a cash vault account for a cash count or cash expense.");
        if (kind == "POS Transaction" && account.AccountType != "POS") throw new ArgumentException("Select a POS account for POS receipts.");
        if (kind == "Account Receipt" && account.AccountType != "CASH") throw new ArgumentException("Select the cash vault for a cash receipt.");
        if (kind == "Account Expense" && account.AccountType == "CASH") throw new ArgumentException("Use Cash Expense for expenses paid from the cash vault.");
        if (kind is "Bank Deposit" or "POS Settlement" or "Account Transfer")
        {
            if (targetId is null || targetId == accountId) throw new ArgumentException("Select a different destination account.");
            var target = await db.Accounts.SingleAsync(x => x.Id == targetId && x.BranchId == _branchId && x.IsActive);
            if (kind == "Bank Deposit" && (account.AccountType != "CASH" || target.AccountType != "BANK")) throw new ArgumentException("A bank deposit must move money from the cash vault to a bank account.");
            if (kind == "POS Settlement" && (account.AccountType != "POS" || target.AccountType != "BANK")) throw new ArgumentException("A POS settlement must move money from a POS account to a bank account.");
        }
        else targetId = null;
        if (kind == "Product Payment")
        {
            if (receiptId is null) throw new ArgumentException("Choose the delivery this supplier payment covers.");
            var receipt = await db.FuelReceipts.SingleAsync(x => x.Id == receiptId && x.BranchId == _branchId);
            var stationCosts = await db.ReceiptShortSettlements.Where(x => x.BranchId == _branchId && x.FuelReceiptId == receipt.Id).SumAsync(x => (decimal?)x.StationBorneCost) ?? 0;
            var paid = await db.AccountTransactions.Where(x => x.BranchId == _branchId && x.FuelReceiptId == receipt.Id && x.TransactionType == "Product Payment").SumAsync(x => (decimal?)x.Amount) ?? 0;
            var due = Math.Max(receipt.TotalCost + stationCosts - paid, 0);
            if (amount > due + 0.005m) throw new ArgumentException($"Payment exceeds the delivery balance due of ₦{due:N2}.");
        }
        else receiptId = null;
        var outflow = kind is "Cash Expense" or "Account Expense" or "Product Payment" or "Account Payment / Amount Sent" or "Bank Deposit" or "POS Settlement" or "Account Transfer";
        var balance = CalculateAccountBalance(account, await db.AccountTransactions.Where(x => x.BranchId == _branchId).ToListAsync(), Date(_transactionDate));
        if (outflow && amount > balance + 0.005m) throw new ArgumentException($"Amount exceeds the {account.Name} balance of ₦{balance:N2}.");
        db.AccountTransactions.Add(new AccountTransaction { BranchId = _branchId, AccountId = accountId, TargetAccountId = targetId, FuelReceiptId = receiptId,
            TransactionType = kind, Amount = amount, Reference = NullIfEmpty(_transactionReference.Text), Description = NullIfEmpty(_transactionDescription.Text),
            OccurredAt = new DateTimeOffset(Date(_transactionDate).ToDateTime(TimeOnly.MinValue), DateTimeOffset.Now.Offset) });
        await db.SaveChangesAsync(); _transactionType.SelectedIndex = 0; Clear(_transactionAmount, _transactionReference, _transactionDescription);
        SetStatus($"{kind} recorded for {account.Name}."); await RefreshAllAsync();
    }

    private async Task SaveCashVaultCountAsync()
    {
        var db = Db(); var cash = await db.Accounts.Where(x => x.BranchId == _branchId && x.IsActive && x.AccountType == "CASH").OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (cash is null) throw new InvalidOperationException("Create an active CASH account before recording the vault count.");
        var amount = Number(_cashVaultCount); if (amount < 0) throw new ArgumentException("Cash vault count cannot be negative.");
        var date = Date(_accountReconcileDate);
        db.AccountTransactions.Add(new AccountTransaction { BranchId = _branchId, AccountId = cash.Id, TransactionType = "Cash Count", Amount = amount, Reference = "Daily physical cash count", OccurredAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), DateTimeOffset.Now.Offset) });
        await db.SaveChangesAsync(); _cashVaultCount.Clear(); SetStatus($"Cash vault counted · ₦{amount:N2} on {date:yyyy-MM-dd}."); await RefreshAllAsync();
    }

    private async Task AddProductAsync()
    {
        var db = Db(); var code = _productCode.Text.Trim().ToUpperInvariant(); var unit = _productUnit.Text.Trim();
        if (code.Length == 0 || _productName.Text.Trim().Length == 0 || unit.Length == 0) throw new ArgumentException("Product code, name and unit are required.");
        if (code == "LPG" && !unit.Equals("kg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("LPG weigh fills are tracked in kilograms (kg) in this version.");
        if (await db.Products.AnyAsync(x => x.Code == code)) throw new ArgumentException($"Product code {code} already exists.");
        db.Products.Add(new FuelProduct { Code = code, Name = _productName.Text.Trim(), UnitOfMeasure = unit,
            IsMetered = !code.Equals("LPG", StringComparison.OrdinalIgnoreCase) });
        await db.SaveChangesAsync(); Clear(_productCode, _productName, _productUnit); SetStatus($"{code} product saved · unit {unit}."); await RefreshAllAsync();
    }

    private async Task AddTankAsync()
    {
        var db = Db(); var product = await db.Products.SingleAsync(x => x.Id == RequiredId(_pumpProduct, "product"));
        var capacity = Number(_tankCapacity); if (capacity <= 0 || string.IsNullOrWhiteSpace(_tankNumber.Text)) throw new ArgumentException("Enter a tank number and positive capacity.");
        var number = _tankNumber.Text.Trim();
        if (await db.Tanks.AnyAsync(x => x.BranchId == _branchId && x.Number == number)) throw new ArgumentException($"Tank {number} already exists in this branch.");
        db.Tanks.Add(new Tank { BranchId = _branchId, Number = number, ProductId = product.Id, CapacityLitres = capacity });
        await db.SaveChangesAsync(); Clear(_tankNumber, _tankCapacity); SetStatus($"Tank saved · capacity {capacity:N2} {product.UnitOfMeasure}."); await RefreshAllAsync();
    }

    private async Task AddPumpAsync()
    {
        var db = Db(); var tankId = RequiredId(_pumpTank, "tank");
        var tank = await db.Tanks.SingleAsync(x => x.Id == tankId); var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId);
        if (!product.IsMetered) throw new ArgumentException("LPG weigh fills use the LPG Fills tab. Add a tank, not a pump, for cylinder fills.");
        if (string.IsNullOrWhiteSpace(_pumpNumber.Text)) throw new ArgumentException("Enter a pump number.");
        var number = _pumpNumber.Text.Trim();
        if (await db.Pumps.AnyAsync(x => x.BranchId == _branchId && x.Number == number)) throw new ArgumentException($"Pump {number} already exists in this branch.");
        db.Pumps.Add(new Pump { BranchId = _branchId, Number = number, TankId = tankId });
        await db.SaveChangesAsync(); Clear(_pumpNumber); SetStatus("Pump saved."); await RefreshAllAsync();
    }

    private async Task AddStaffAsync()
    {
        if (string.IsNullOrWhiteSpace(_staffName.Text)) throw new ArgumentException("Enter an attendant name.");
        var salary = Number(_staffSalary);
        if (!string.IsNullOrWhiteSpace(_staffPayday.Text) && (!int.TryParse(_staffPayday.Text.Trim(), out var payday) || payday is < 1 or > 31))
            throw new ArgumentException("Salary payday must be a day from 1 to 31.");
        DateOnly? joined = null;
        if (!string.IsNullOrWhiteSpace(_staffJoinDate.Text))
        {
            joined = DateOnly.ParseExact(_staffJoinDate.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (joined > DateOnly.FromDateTime(DateTime.Today)) throw new ArgumentException("Date joined cannot be in the future.");
        }
        var staff = new StaffAttendant { BranchId = _branchId, Name = _staffName.Text.Trim(), Phone = NullIfEmpty(_staffPhone.Text), DateJoined = joined, MonthlySalary = salary,
            SalaryPaydayDay = int.TryParse(_staffPayday.Text.Trim(), out var day) ? day : null };
        Db().StaffAttendants.Add(staff);
        await Db().SaveChangesAsync(); Clear(_staffName, _staffPhone, _staffJoinDate, _staffPayday); _staffSalary.Text = "0"; SetStatus("Attendant saved · ready to assign to a session."); await RefreshAllAsync();
    }

    private async Task AddAccountAsync()
    {
        var name = _accountName.Text.Trim(); var type = _accountTypePicker.SelectedItem?.ToString() ?? "";
        if (name.Length == 0 || !new[] { "CASH", "BANK", "POS" }.Contains(type)) throw new ArgumentException("Enter an account name and choose CASH, BANK or POS as its type.");
        if (await Db().Accounts.AnyAsync(x => x.BranchId == _branchId && x.Name == name && x.AccountType == type))
            throw new ArgumentException("An account with this name and type already exists in the branch.");
        var opening = string.IsNullOrWhiteSpace(_accountOpeningBalance.Text) ? 0 : Number(_accountOpeningBalance);
        if (opening < 0) throw new ArgumentException("Opening balance cannot be negative.");
        var db = Db(); var account = new Account { BranchId = _branchId, Name = name, AccountType = type, AccountNumber = NullIfEmpty(_accountNumber.Text), Provider = NullIfEmpty(_accountProvider.Text) };
        db.Accounts.Add(account); await db.SaveChangesAsync();
        if (opening > 0) db.AccountTransactions.Add(new AccountTransaction { BranchId = _branchId, AccountId = account.Id, TransactionType = "Opening Balance", Amount = opening, Reference = "Opening balance", OccurredAt = DateTimeOffset.Now });
        await db.SaveChangesAsync(); Clear(_accountName, _accountNumber, _accountProvider, _accountOpeningBalance); _accountTypePicker.SelectedIndex = 0; SetStatus(opening > 0 ? $"Account added with opening balance ₦{opening:N2}." : "Account added."); await RefreshAllAsync();
    }

    private async Task FillPickerAsync(ComboBox picker, List<Choice> choices, bool optional = false)
    {
        picker.ItemsSource = optional ? new[] { new Choice(0, "Unassigned") }.Concat(choices).ToList() : choices;
        picker.DisplayMemberPath = nameof(Choice.Label); picker.SelectedValuePath = nameof(Choice.Id);
        if (picker.Items.Count > 0) picker.SelectedIndex = 0;
        await Task.CompletedTask;
    }

    private StackPanel FormArea(Grid host, string title, string description)
    {
        var root = PageLayout(host, title, description);
        var form = new StackPanel { Margin = new Thickness(0, 4, 0, 8) }; root.Children.Add(form); return form;
    }

    private StackPanel PageLayout(Grid host, string title, string description)
    {
        host.Children.Clear(); host.RowDefinitions.Clear(); host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); host.RowDefinitions.Add(new RowDefinition());
        var outer = new StackPanel(); Grid.SetRow(outer, 0); host.Children.Add(outer);
        outer.Children.Add(Heading(title, description));
        return outer;
    }

    private static StackPanel Heading(string title, string description)
    {
        var s = new StackPanel { Margin = new Thickness(4, 0, 4, 8) };
        s.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(23, 43, 77)) });
        s.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 5, 0, 0) });
        return s;
    }
    private static StackPanel SetupCard(string title, string description)
    {
        var card = new StackPanel { Margin = new Thickness(5), Background = Brushes.White };
        card.Children.Add(Subheading(title));
        card.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), FontSize = 12, Margin = new Thickness(5, 0, 5, 9), MinHeight = 34 });
        card.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(234, 236, 240)), Margin = new Thickness(4, 0, 4, 7) });
        return card;
    }
    private static TextBlock Subheading(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 6, 4, 5) };
    private static TextBox Field(string hint) => new() { ToolTip = hint, Tag = hint, MinWidth = 138 };
    private static ComboBox Picker(bool optional = false) => new() { MinWidth = 170, Tag = optional ? "Attendant (optional)" : "Select item", ToolTip = optional ? "Select an attendant or leave unassigned" : "Select an item" };
    private static TextBox DateField() => new() { Text = DateTime.Today.ToString("yyyy-MM-dd"), ToolTip = "Transaction date (YYYY-MM-DD)", MinWidth = 135 };
    private static DateOnly Date(TextBox input) => DateOnly.TryParse(input.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d : throw new ArgumentException($"Enter a valid date (YYYY-MM-DD) for {input.ToolTip}.");

    private static void AddFields(Panel panel, params Control[] controls)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var c in controls)
        {
            var field = new StackPanel { Margin = new Thickness(3), MinWidth = 150, MaxWidth = 245 };
            var label = c is TextBox tb ? (tb.Tag?.ToString() ?? tb.ToolTip?.ToString() ?? "Entry") : (c.Tag?.ToString() ?? c.ToolTip?.ToString() ?? "Select");
            field.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)), Margin = new Thickness(3, 0, 0, 2) });
            if (c is TextBox textBox) { textBox.Height = 34; textBox.HorizontalAlignment = HorizontalAlignment.Stretch; }
            if (c is ComboBox combo) { combo.Height = 36; combo.HorizontalAlignment = HorizontalAlignment.Stretch; }
            field.Children.Add(c); wrap.Children.Add(field);
        }
        panel.Children.Add(wrap);
    }
    private void AddAction(Panel panel, string text, Func<Task> action)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(5, 2, 5, 8) };
        b.Click += async (_, _) => await GuardAsync(action); panel.Children.Add(b);
    }
    private async Task ExportReportCsvAsync()
    {
        await LoadReportAsync();
        var rows = CurrentReportRows();
        if (rows.Count == 0) throw new InvalidOperationException("There are no report rows to export yet.");
        var dialog = new SaveFileDialog { Title = "Export station report", Filter = "CSV file (*.csv)|*.csv", FileName = $"FuelStationERP-report-{DateTime.Today:yyyy-MM-dd}.csv", DefaultExt = ".csv", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        static string Cell(object? value)
        {
            var text = value switch { null => "", DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), decimal n => n.ToString("0.00", CultureInfo.InvariantCulture), _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var lines = new List<string> { string.Join(",", ReportHeaders.Select(Cell)) };
        lines.AddRange(rows.Select(row => string.Join(",", ReportValues(row).Select(Cell))));
        await File.WriteAllTextAsync(dialog.FileName, string.Join(Environment.NewLine, lines), new UTF8Encoding(true));
        await ArchiveReportDocumentAsync(dialog.FileName);
        SetStatus($"Report exported · {rows.Count} rows · {dialog.FileName}");
    }

    private async Task ExportReportExcelAsync()
    {
        await LoadReportAsync();
        var rows = CurrentReportRows();
        if (rows.Count == 0) throw new InvalidOperationException("There are no report rows to export yet.");
        var dialog = new SaveFileDialog { Title = "Export station report to Excel", Filter = "Excel Workbook (*.xlsx)|*.xlsx", FileName = $"CLEON-station-report-{DateTime.Today:yyyy-MM-dd}.xlsx", DefaultExt = ".xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        var from = Date(_reportFrom); var to = Date(_reportTo); var branch = BranchName;
        await Task.Run(() => WriteExcelReport(dialog.FileName, rows, from, to, branch));
        await ArchiveReportDocumentAsync(dialog.FileName);
        SetStatus($"Excel report exported · {rows.Count:N0} rows · {dialog.FileName}");
    }

    private async Task ExportReportPdfAsync()
    {
        await LoadReportAsync();
        var rows = CurrentReportRows();
        if (rows.Count == 0) throw new InvalidOperationException("There are no report rows to export yet.");
        var dialog = new SaveFileDialog { Title = "Export station report to PDF", Filter = "PDF document (*.pdf)|*.pdf", FileName = $"CLEON-station-report-{DateTime.Today:yyyy-MM-dd}.pdf", DefaultExt = ".pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        var from = Date(_reportFrom); var to = Date(_reportTo); var branch = BranchName;
        await Task.Run(() => WritePdfReport(dialog.FileName, rows, from, to, branch));
        await ArchiveReportDocumentAsync(dialog.FileName);
        SetStatus($"PDF report exported · {rows.Count:N0} rows · {dialog.FileName}");
    }

    private string BranchName => BranchSelector.SelectedItem is Choice choice ? choice.Label : "Station";

    private List<ReportRow> CurrentReportRows() =>
        (_reportGrid.ItemsSource as System.Collections.IEnumerable)?.Cast<ReportRow>().ToList() ?? [];

    private static readonly string[] ReportHeaders = ["Type", "Date", "Product / category", "Quantity", "Unit", "Sales / expense (NGN)", "Cost of goods sold (NGN)", "Gross profit (NGN)", "Short / over (NGN)"];

    private static object?[] ReportValues(ReportRow row) => [row.Type, row.Date, row.Product, row.Quantity, row.Unit, row.Sales, row.CostOfGoodsSold, row.GrossProfit, row.ShortOver];

    private static void WriteExcelReport(string path, IReadOnlyList<ReportRow> rows, DateOnly from, DateOnly to, string branch)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheetData = new XElement(main + "sheetData");
        void TextRow(int rowNumber, string[] values, uint style = 0)
        {
            var cells = values.Select((value, index) => new XElement(main + "c", new XAttribute("r", ExcelCellRef(index + 1, rowNumber)), new XAttribute("t", "inlineStr"), style == 0 ? null : new XAttribute("s", style), new XElement(main + "is", new XElement(main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value))));
            sheetData.Add(new XElement(main + "row", new XAttribute("r", rowNumber), cells));
        }
        void ValueRow(int rowNumber, object?[] values, bool header = false)
        {
            var cells = new List<XElement>();
            for (var i = 0; i < values.Length; i++)
            {
                var reference = ExcelCellRef(i + 1, rowNumber); var value = values[i];
                if (value is decimal number)
                    cells.Add(new XElement(main + "c", new XAttribute("r", reference), new XAttribute("s", 2), new XElement(main + "v", number.ToString("0.00", CultureInfo.InvariantCulture))));
                else
                    cells.Add(new XElement(main + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"), header ? new XAttribute("s", 1) : null,
                        new XElement(main + "is", new XElement(main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""))));
            }
            sheetData.Add(new XElement(main + "row", new XAttribute("r", rowNumber), cells));
        }

        TextRow(1, ["CLEON · FuelStationERP Report Center"]);
        TextRow(2, ["Branch", branch]);
        TextRow(3, ["Period", $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}"]);
        var salesRows = rows.Where(x => x.Type is "Pump session" or "LPG fill" or "Attendant activity").ToList();
        var sales = salesRows.Sum(x => x.Sales);
        var expenses = -rows.Where(x => x.Type == "Expense").Sum(x => x.Sales);
        ValueRow(4, ["Fuel and LPG sales (NGN)", sales]);
        ValueRow(5, ["Cost of goods sold (NGN)", salesRows.Sum(x => x.CostOfGoodsSold)]);
        ValueRow(6, ["Gross profit before operating expenses (NGN)", salesRows.Sum(x => x.GrossProfit)]);
        ValueRow(7, ["Operating expenses (NGN)", expenses]);
        ValueRow(8, ["Short / over (NGN)", salesRows.Sum(x => x.ShortOver)]);
        ValueRow(10, ReportHeaders.Cast<object?>().ToArray(), header: true);
        for (var i = 0; i < rows.Count; i++) ValueRow(i + 11, ReportValues(rows[i]));

        var workbook = new XDocument(new XElement(main + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", rel),
            new XElement(main + "sheets", new XElement(main + "sheet", new XAttribute("name", "Station Report"), new XAttribute("sheetId", 1), new XAttribute(rel + "id", "rId1")))));
        var worksheet = new XDocument(new XElement(main + "worksheet", sheetData));
        var styles = XDocument.Parse("""
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00;[Red]-#,##0.00"/></numFmts>
              <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="10"/><name val="Calibri"/></font></fonts>
              <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF101828"/><bgColor indexed="64"/></patternFill></fill></fills>
              <borders count="1"><border><left/><right/><top/><bottom/></border></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/><xf numFmtId="0" fontId="1" fillId="1" borderId="0" applyFont="1" applyFill="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" applyNumberFormat="1"/></cellXfs>
              <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
            </styleSheet>
            """);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        AddXml(archive, "[Content_Types].xml", XDocument.Parse("""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """));
        AddXml(archive, "_rels/.rels", XDocument.Parse("""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """));
        AddXml(archive, "xl/workbook.xml", workbook);
        AddXml(archive, "xl/_rels/workbook.xml.rels", XDocument.Parse("""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """));
        AddXml(archive, "xl/worksheets/sheet1.xml", worksheet);
        AddXml(archive, "xl/styles.xml", styles);
    }

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open(); document.Save(stream);
    }

    private static string ExcelCellRef(int column, int row)
    {
        var name = "";
        while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
        return name + row.ToString(CultureInfo.InvariantCulture);
    }

    private static void WritePdfReport(string path, IReadOnlyList<ReportRow> rows, DateOnly from, DateOnly to, string branch)
    {
        const double pageWidth = 842, pageHeight = 595, left = 34;
        double[] widths = [78, 60, 118, 65, 38, 97, 97, 97, 124];
        var xs = new double[widths.Length + 1]; xs[0] = left;
        for (var i = 0; i < widths.Length; i++) xs[i + 1] = xs[i] + widths[i];
        var pages = new List<byte[]>(); const int rowsPerPage = 22;
        var pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));
        var salesRows = rows.Where(x => x.Type is "Pump session" or "LPG fill" or "Attendant activity").ToList();
        var sales = salesRows.Sum(x => x.Sales);
        var cogs = salesRows.Sum(x => x.CostOfGoodsSold);
        var grossProfit = salesRows.Sum(x => x.GrossProfit);
        var expenses = -rows.Where(x => x.Type == "Expense").Sum(x => x.Sales);
        var shortOver = salesRows.Sum(x => x.ShortOver);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var content = new StringBuilder();
            PdfText(content, left, 562, "CLEON  |  FUEL STATION ERP", 16, true, "0.06 0.09 0.16");
            PdfText(content, left, 542, "Station performance report  ·  " + branch, 10, true, "0.12 0.18 0.25");
            PdfText(content, left, 526, $"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}     Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", 8, false, "0.35 0.40 0.46");
            double headerY;
            if (pageIndex == 0)
            {
                var metricNames = new[] { "Sales NGN", "COGS NGN", "Gross profit", "Expenses NGN", "Short / over" };
                var metricValues = new[] { sales, cogs, grossProfit, expenses, shortOver };
                const double cardWidth = 148, gap = 8, cardY = 486, cardHeight = 30;
                for (var i = 0; i < metricNames.Length; i++)
                {
                    var x = left + i * (cardWidth + gap);
                    content.Append("0.95 0.96 0.97 rg ").Append(F(x)).Append(' ').Append(F(cardY)).Append(' ').Append(F(cardWidth)).Append(' ').Append(F(cardHeight)).AppendLine(" re f");
                    PdfText(content, x + 7, cardY + 18, metricNames[i], 6.5, false, "0.35 0.40 0.46");
                    PdfText(content, x + 7, cardY + 6, metricValues[i].ToString("N2", CultureInfo.InvariantCulture), 8, true, "0.06 0.09 0.16");
                }
                headerY = 466;
            }
            else headerY = 506;

            content.Append("0.06 0.09 0.16 rg ").Append(F(left)).Append(' ').Append(F(headerY - 18)).Append(' ').Append(F(xs[^1] - left)).AppendLine(" 22 re f");
            var headers = new[] { "Type", "Date", "Product / category", "Quantity", "Unit", "Sales / expense", "Cost of goods", "Gross profit", "Short / over" };
            for (var i = 0; i < headers.Length; i++) PdfText(content, xs[i] + 4, headerY - 10, headers[i], 6.3, true, "1 1 1");
            var start = pageIndex * rowsPerPage; var count = Math.Min(rowsPerPage, rows.Count - start); var y = headerY - 35;
            for (var r = 0; r < count; r++, y -= 17)
            {
                var row = rows[start + r];
                if (r % 2 == 1) content.Append("0.97 0.98 0.99 rg ").Append(F(left)).Append(' ').Append(F(y - 5)).Append(' ').Append(F(xs[^1] - left)).AppendLine(" 16 re f");
                content.Append("0.88 0.90 0.92 RG 0.4 w ").Append(F(left)).Append(' ').Append(F(y - 6)).Append(" m ").Append(F(xs[^1])).Append(' ').Append(F(y - 6)).AppendLine(" l S");
                var values = new[] { row.Type, row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Product, row.Quantity.ToString("N2", CultureInfo.InvariantCulture), row.Unit,
                    row.Sales.ToString("N2", CultureInfo.InvariantCulture), row.CostOfGoodsSold.ToString("N2", CultureInfo.InvariantCulture), row.GrossProfit.ToString("N2", CultureInfo.InvariantCulture), row.ShortOver.ToString("N2", CultureInfo.InvariantCulture) };
                for (var col = 0; col < values.Length; col++)
                {
                    var text = values[col];
                    var max = Math.Max(5, (int)(widths[col] / 4.2));
                    if (text.Length > max) text = text[..Math.Max(1, max - 1)] + "…";
                    var rightAligned = col is 3 or >= 5;
                    var x = rightAligned ? xs[col + 1] - 4 - text.Length * 3.6 : xs[col] + 4;
                    PdfText(content, x, y, text, 7, false, "0.15 0.19 0.24");
                }
            }
            PdfText(content, left, 22, $"CLEON Station Operations   ·   Page {pageIndex + 1} of {pageCount}", 7, false, "0.40 0.44 0.49");
            pages.Add(Encoding.ASCII.GetBytes(content.ToString()));
        }

        var objects = new List<byte[]> { Array.Empty<byte>() };
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Pages /Count " + pageCount + " /Kids [" + string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{5 + 2 * i} 0 R")) + "] >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"));
        for (var i = 0; i < pages.Count; i++)
        {
            var contentId = 6 + 2 * i;
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageWidth)} {F(pageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>"));
            var stream = pages[i];
            using var buffer = new MemoryStream();
            var prefix = Encoding.ASCII.GetBytes($"<< /Length {stream.Length} >>\nstream\n"); buffer.Write(prefix); buffer.Write(stream); buffer.Write(Encoding.ASCII.GetBytes("\nendstream"));
            objects.Add(buffer.ToArray());
        }
        using var output = File.Create(path); output.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n%CLEON\n"));
        var offsets = new long[objects.Count];
        for (var id = 1; id < objects.Count; id++) { offsets[id] = output.Position; output.Write(Encoding.ASCII.GetBytes($"{id} 0 obj\n")); output.Write(objects[id]); output.Write(Encoding.ASCII.GetBytes("\nendobj\n")); }
        var xref = output.Position; output.Write(Encoding.ASCII.GetBytes($"xref\n0 {objects.Count}\n0000000000 65535 f \n"));
        for (var id = 1; id < offsets.Length; id++) output.Write(Encoding.ASCII.GetBytes(offsets[id].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n"));
        output.Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {objects.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"));
    }

    private static void PdfText(StringBuilder builder, double x, double y, string text, double size, bool bold, string color)
    {
        var safe = new string(text.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        builder.Append(color).Append(" rg BT /").Append(bold ? "F2" : "F1").Append(' ').Append(F(size)).Append(" Tf 1 0 0 1 ").Append(F(x)).Append(' ').Append(F(y)).Append(" Tm (").Append(safe).AppendLine(") Tj ET");
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private DataGrid AddGrid(Grid host)
    {
        var grid = GridView(); Grid.SetRow(grid, 1); host.Children.Add(grid); _grids.Add(grid); return grid;
    }
    private static DataGrid GridView()
    {
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, AlternationCount = 2 };
        grid.AutoGeneratingColumn += (_, e) => e.Column.Header = e.PropertyName switch
        {
            "Id" => "#", "Type" => "Record type", "Product" => "Product / category", "Quantity" => "Quantity", "Unit" => "Unit",
            "Sales" => "Sales / expense", "Date" => "Date", "ProductCodeSnapshot" => "Product", "UnitOfMeasureSnapshot" => "Unit",
            "SupplierNameSnapshot" => "Supplier", "CostOfGoodsSold" => "Cost of fuel sold", "GrossProfit" => "Gross profit",
            "ShortOver" => "Short / over", "PosAmount" => "POS", "PricePerLitre" or "UnitPrice" => "Price / unit",
            "InvoiceLitres" => "Invoice qty", "ReceivedLitres" => "Received qty", "RemainingLitres" => "Remaining qty",
            "UnitCost" => "Cost / unit", "RttLitres" => "RTT qty", "SessionDate" or "ReceiptDate" or "SaleDate" => "Date",
            "CylinderCount" => "Cylinders", "CylinderSize" => "Cylinder size", "AverageCostPerLitre" => "Average cost / unit",
            _ => System.Text.RegularExpressions.Regex.Replace(e.PropertyName, "(?<=[a-z0-9])(?=[A-Z])", " ")
        };
        return grid;
    }
    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { SetStatus("Entry needs attention"); MessageBox.Show(ex.GetBaseException().Message, "Check this entry", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private static decimal Number(TextBox input)
    {
        if (!decimal.TryParse(input.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var n) &&
            !decimal.TryParse(input.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out n))
            throw new ArgumentException($"Enter a valid number for {input.Tag ?? input.ToolTip}.");
        if (n < 0) throw new ArgumentException($"{input.Tag ?? input.ToolTip} cannot be negative.");
        return n;
    }
    private static long RequiredId(ComboBox picker, string label) => SelectedId(picker) ?? throw new ArgumentException($"Select a {label}.");
    private static long? SelectedId(ComboBox picker) => picker.SelectedValue is long n && n > 0 ? n : null;
    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static void Clear(params TextBox[] fields) { foreach (var f in fields) f.Clear(); }
    private ErpDbContext Db() => _db ?? throw new InvalidOperationException("Station database is not ready.");
    private void SetStatus(string text) => StatusText.Text = text;
    private void SetMetric(string name, string value)
    {
        if (FindName(name) is TextBlock block) block.Text = value;
        else
        {
            var label = OverviewHost.Children.OfType<UniformGrid>().FirstOrDefault()?.Children.OfType<Border>()
                .SelectMany(b => ((StackPanel)b.Child).Children.OfType<TextBlock>()).FirstOrDefault(t => t.Name == name);
            if (label is not null) label.Text = value;
        }
    }
    private sealed record Choice(long Id, string Label);
    private sealed record ChartBar(string Label, decimal Value, string Display, string Color);
    private sealed record SetupRow(string Type, string Name, string Details, string Additional);
    private sealed record ReportRow(string Type, DateOnly Date, string Product, decimal Quantity, string Unit, decimal Sales, decimal CostOfGoodsSold, decimal GrossProfit, decimal ShortOver);
}
