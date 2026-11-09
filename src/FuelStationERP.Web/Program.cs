using System.Net.Http.Headers;
using System.Security.Claims;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
var apiBase = builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7290";
builder.Services.AddHttpClient("erp", client => client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/login"; o.LogoutPath = "/logout"; o.SlidingExpiration = true; o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization();

app.MapGet("/login", () => Html(LoginPage())).AllowAnonymous();
app.MapPost("/login", async (HttpContext context, IHttpClientFactory clients) =>
{
    var form = await context.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var api = clients.CreateClient("erp");
    var response = await api.PostAsJsonAsync("api/v1/auth/login", new { username, password });
    if (!response.IsSuccessStatusCode) return Html(LoginPage("The username or password was not accepted."));
    var json = await response.Content.ReadFromJsonAsync<JsonObject>();
    var jwt = json?["accessToken"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(jwt)) return Html(LoginPage("The API did not return an access token."));
    var claims = JwtClaims(jwt).ToList(); claims.Add(new Claim("erp_jwt", jwt)); claims.Add(new Claim(ClaimTypes.Name, username));
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
}).AllowAnonymous();

app.MapPost("/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.Redirect("/login"); }).AllowAnonymous();

app.MapGet("/", async (HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    using var response = await ApiGet(clients, auth, "api/v1/branches");
    if (!response.IsSuccessStatusCode) return Html(Layout("Station dashboard", Error(response), auth, "<p>Could not load branches from the API.</p>"));
    var branches = JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsArray() ?? [];
    var body = $"<section class='hero'><span class='eyebrow'>CLEON · OPERATIONS</span><h1>Your stations, at a glance</h1><p>Choose a branch to review today's performance, manage station activity and keep every location moving.</p></section><div class='section-heading'><div><h2>Stations</h2><p>{branches.Count} {(branches.Count == 1 ? "branch" : "branches")} connected to this workspace</p></div><a class='export-button quiet-link' href='/admin/branches'>Manage branches</a></div><section class='grid'>";
    foreach (var branch in branches)
    {
        var id = Val(branch, "id");
        var code = Val(branch, "code");
        body += $"<a class='tile branch-card' href='/branches/{E(id)}'><span class='branch-mark'>{E(code.Length > 0 ? code[..Math.Min(code.Length, 2)] : "BR")}</span><span class='eyebrow'>BRANCH · {E(code)}</span><h2>{E(Val(branch, "name"))}</h2><span class='link'>Open station dashboard <span aria-hidden='true'>→</span></span></a>";
    }
    body += "</section><div class='section-heading'><div><h2>Product catalog</h2><p>Products available across your station network</p></div><a class='export-button quiet-link' href='/admin/products'>Manage products</a></div><section class='grid'>";
    using var products = await ApiGet(clients, auth, "api/v1/products");
    if (products.IsSuccessStatusCode)
    {
        var rows = JsonNode.Parse(await products.Content.ReadAsStringAsync())?.AsArray() ?? [];
        foreach (var product in rows)
        {
            var metered = bool.TryParse(Val(product, "isMetered"), out var isMetered) && isMetered;
            body += $"<article class='tile product-card'><span class='eyebrow'>{E(Val(product, "code"))} · {E(Val(product, "unitOfMeasure"))}</span><h3>{E(Val(product, "name"))}</h3><span class='chip'>{(metered ? "Metered sales" : "Non-metered sales")}</span></article>";
        }
    }
    else body += "<p class='empty'>The product catalog could not be loaded.</p>";
    body += "</section>";
    return Html(Layout("Station dashboard", "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapGet("/branches/{branchId:long}", async (long branchId, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    using var response = await ApiGet(clients, auth, $"api/v1/branches/{branchId}/dashboard");
    if (!response.IsSuccessStatusCode) return Html(Layout("Branch dashboard", "FuelStationERP · Web", auth, Error(response)));
    var data = JsonNode.Parse(await response.Content.ReadAsStringAsync());
    var body = $"<section class='hero'><span class='eyebrow'>BRANCH · {branchId} · {E(Val(data, "date"))}</span><h1>Today's operations</h1><p>Live totals from posted sales, stock cost, expenses and cash reconciliation.</p></section>{BranchNav(branchId, "dashboard")}<section class='grid metrics'>{Metric("Fuel sales", Money(data, "fuelSales"))}{Metric("Other product sales", Money(data, "otherProductSales"))}{Metric("Gross profit", Money(data, "grossProfit"))}{Metric("Expenses", Money(data, "expenses"))}{Metric("Net operating profit", Money(data, "netOperatingProfit"))}{Metric("Short / over", Money(data, "shortOver"))}</section><section class='panel'><h2>Recent pump sessions</h2>{RenderRecentSessions(data?["recentSessions"] as JsonArray)}</section><div class='section-heading'><div><h2>Station operations</h2><p>Quick access to the daily tools for this branch</p></div></div><section class='grid links'>";
    foreach (var item in new[] { ("pump-sessions", "Pump sessions", "Post and review metered sales."), ("fuel-receipts", "Fuel deliveries", "Record deliveries and reconcile shortages."), ("product-sales", "LPG fills", "Record weighed product fills."), ("tank-dips", "Stock checks", "Compare physical readings with book stock."), ("expenses", "Expenses", "Capture and review station costs."), ("accounts", "Accounts & cash", "Review balances and daily reconciliation."), ("pump-runs", "Pump runs", "Open and close pump meter runs."), ("tanks", "Tanks", "Manage tank setup and capacities."), ("pumps", "Pumps", "Manage pump-to-tank assignments."), ("attendants", "Attendants", "Manage station attendant records."), ("suppliers", "Suppliers", "Maintain fuel supplier details."), ("product-prices", "Selling prices", "Set effective product prices.") })
        body += $"<a class='tile compact' href='/branches/{branchId}/{item.Item1}'><h3>{item.Item2}</h3><p>{item.Item3}</p><span class='link'>Open workspace <span aria-hidden='true'>→</span></span></a>";
    body += $"<a class='tile compact' href='/branches/{branchId}/reports'><h3>Report Center</h3><p>Analyze performance and export reports.</p><span class='link'>Open workspace <span aria-hidden='true'>→</span></span></a>";
    body += "</section>";
    return Html(Layout("Branch dashboard", "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapGet("/branches/{branchId:long}/{resource}", async (long branchId, string resource, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    var allowed = new Dictionary<string, string> { ["accounts"] = "accounts", ["account-transactions"] = "account-transactions", ["tanks"] = "tanks", ["pumps"] = "pumps", ["attendants"] = "attendants", ["suppliers"] = "suppliers", ["fuel-receipts"] = "fuel-receipts", ["expenses"] = "expenses", ["pump-runs"] = "pump-runs", ["pump-sessions"] = "pump-sessions", ["tank-dips"] = "tank-dips", ["product-sales"] = "product-sales", ["product-prices"] = "product-prices" };
    if (!allowed.TryGetValue(resource, out var endpoint)) return Results.NotFound();
    using var response = await ApiGet(clients, auth, $"api/v1/branches/{branchId}/{endpoint}");
    var payload = response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync()) : null;
    if (resource == "fuel-receipts" && payload is JsonArray receiptRows)
        foreach (var row in receiptRows.OfType<JsonObject>())
            row["fuelShortage"] = Math.Max(Dec(row["invoiceLitres"]) - Dec(row["receivedLitres"]), 0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    if (resource == "accounts" && response.IsSuccessStatusCode && payload is JsonArray accountRows)
    {
        var bodyAccounts = $"<section class='hero'><a class='back' href='/branches/{branchId}'>← Branch dashboard</a><span class='eyebrow'>CASH · POS · BANK</span><h1>Accounts & reconciliation</h1><p>Named account balances, receipt-linked supplier payments, Cash Vault counts and daily cash/POS reconciliation.</p><a class='export-button' href='/branches/{branchId}/account-reconciliation?date={DateTime.Today:yyyy-MM-dd}'>Open today's reconciliation</a></section>{BranchNav(branchId, resource)}<section class='panel'><h2>Account balances</h2>{RenderRecords(accountRows)}</section>";
        bodyAccounts += FormFor("accounts", branchId, new Dictionary<string, string>());
        var txnData = await LoadFormDataAsync("account-transactions", branchId, auth, clients);
        bodyAccounts += FormFor("account-transactions", branchId, txnData);
        using var txResponse = await ApiGet(clients, auth, $"api/v1/branches/{branchId}/account-transactions");
        if (txResponse.IsSuccessStatusCode) bodyAccounts += $"<section class='panel'><h2>Recent account ledger</h2>{RenderRecords(JsonNode.Parse(await txResponse.Content.ReadAsStringAsync()) as JsonArray)}</section>";
        return Html(Layout("Accounts & reconciliation", "FuelStationERP · Web", auth, bodyAccounts));
    }
    var title = resource.Replace('-', ' ');
    var formData = await LoadFormDataAsync(resource, branchId, auth, clients);
    var body = $"<section class='hero'><a class='back' href='/branches/{branchId}'>← Branch dashboard</a><span class='eyebrow'>BRANCH · {branchId}</span><h1>{E(title)}</h1><p>Records are saved to the shared ERP API.</p></section>{BranchNav(branchId, resource)}";
    body += response.IsSuccessStatusCode ? (resource == "fuel-receipts" ? RenderReceiptRows(payload) : RenderRecords(payload)) : Error(response);
    if (resource == "pump-runs" && response.IsSuccessStatusCode && payload is JsonArray runs)
        foreach (var run in runs.Where(x => Val(x, "status").Equals("OPEN", StringComparison.OrdinalIgnoreCase)))
            body += $"<section class='panel'><h2>Close run {E(Val(run, "id"))} · pump {E(Val(run, "pumpId"))}</h2><form method='post' action='/branches/{branchId}/pump-runs/{E(Val(run, "id"))}/close' class='form-grid'><label>Closing meter<input name='closingMeter' type='number' step='0.01' required></label><button>Close run</button></form></section>";
    if (resource == "fuel-receipts" && payload is JsonArray receiptRecords)
        foreach (var row in receiptRecords.OfType<JsonObject>().Where(x => Dec(x["fuelShortage"]) > 0))
            body += ReceiptShortForm(branchId, row);
    body += FormFor(resource, branchId, formData);
    return Html(Layout(title, "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapGet("/branches/{branchId:long}/account-reconciliation", async (long branchId, DateOnly? date, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    var day = date ?? DateOnly.FromDateTime(DateTime.Today);
    using var response = await ApiGet(clients, auth, $"api/v1/branches/{branchId}/account-reconciliation?date={day:yyyy-MM-dd}");
    if (!response.IsSuccessStatusCode) return Html(Layout("Daily cash reconciliation", "FuelStationERP · Web", auth, Error(response)));
    var data = JsonNode.Parse(await response.Content.ReadAsStringAsync());
    var body = $"<section class='hero'><a class='back' href='/branches/{branchId}/accounts'>← Accounts</a><span class='eyebrow'>DAILY RECONCILIATION</span><h1>Cash & POS · {day:yyyy-MM-dd}</h1><p>Compare session collections, physical vault count, cash expenses, POS receipts and bank movements.</p></section>{BranchNav(branchId, "accounts")}<section class='panel'><form method='get' class='form-grid'><label>Reconciliation date<input name='date' type='date' value='{day:yyyy-MM-dd}' required></label><button>Refresh</button></form><div class='grid metrics'>{Metric("Expected cash", Money(data, "expectedCash"))}{Metric("Expected POS", Money(data, "expectedPos"))}{Metric("Cash collected", Money(data, "cashCollected"))}{Metric("POS collected", Money(data, "posCollected"))}{Metric("Vault count", Money(data, "cashVaultCount"))}{Metric("Cash pending", Money(data, "cashPending"))}{Metric("POS pending", Money(data, "posPending"))}{Metric("Status", Val(data, "status"))}</div><p>Collection gap: {Money(data, "cashCollectionGap")} · Cash expenses: {Money(data, "cashExpense")} · Pump short / over: {Money(data, "pumpShortOver")}</p></section><section class='panel'><h2>Record physical cash count</h2><p>Post a Cash Count transaction from Accounts & reconciliation. Counts anchor the Cash Vault balance for this date.</p><a class='export-button' href='/branches/{branchId}/accounts'>Open account ledger</a></section>";
    return Html(Layout("Daily cash reconciliation", "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapPost("/branches/{branchId:long}/fuel-receipts/{receiptId:long}/short-reconciliation", async (long branchId, long receiptId, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    var form = await context.Request.ReadFormAsync();
    DateOnly.TryParse(form["settlementDate"].ToString(), out var settlementDate);
    var payload = new { partyType = form["partyType"].ToString(), partyName = form["partyName"].ToString(), reconciledLitres = Dec(form["reconciledLitres"].ToString()), reconciledCost = Dec(form["reconciledCost"].ToString()), stationBorneLitres = Dec(form["stationBorneLitres"].ToString()), stationBorneCost = Dec(form["stationBorneCost"].ToString()), disputed = form["disputed"].ToString() == "on", settlementDate = settlementDate == default ? DateOnly.FromDateTime(DateTime.Today) : settlementDate };
    using var response = await Send(clients, auth, new HttpRequestMessage(HttpMethod.Post, $"api/v1/branches/{branchId}/fuel-receipts/{receiptId}/short-reconciliation") { Content = JsonContent.Create(payload) });
    if (response.IsSuccessStatusCode) return Results.Redirect($"/branches/{branchId}/fuel-receipts");
    var detail = E(await response.Content.ReadAsStringAsync());
    return Html(Layout("Could not reconcile delivery shortage", "FuelStationERP · Web", auth, $"<section class='panel error'><h2>Could not save reconciliation</h2><pre>{detail}</pre><a href='/branches/{branchId}/fuel-receipts'>Return to deliveries</a></section>"));
}).RequireAuthorization();

app.MapPost("/branches/{branchId:long}/pump-runs/{runId:long}/close", async (long branchId, long runId, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    var form = await context.Request.ReadFormAsync();
    if (!decimal.TryParse(form["closingMeter"], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var meter)) return Results.BadRequest("Enter a valid closing meter.");
    using var response = await Send(clients, auth, new HttpRequestMessage(HttpMethod.Post, $"api/v1/branches/{branchId}/pump-runs/{runId}/close") { Content = JsonContent.Create(new { closingMeter = meter }) });
    return response.IsSuccessStatusCode ? Results.Redirect($"/branches/{branchId}/pump-runs") : Html(Layout("Could not close pump run", "FuelStationERP · Web", auth, $"<section class='panel error'><pre>{E(await response.Content.ReadAsStringAsync())}</pre></section>"));
}).RequireAuthorization();

app.MapPost("/branches/{branchId:long}/{resource}", async (long branchId, string resource, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    var form = await context.Request.ReadFormAsync();
    var payload = FormPayload(resource, form);
    if (payload is null) return Results.BadRequest("This record type is not supported.");
    using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/branches/{branchId}/{resource}") { Content = JsonContent.Create(payload) };
    using var response = await Send(clients, auth, request);
    if (!response.IsSuccessStatusCode)
    {
        var detail = E(await response.Content.ReadAsStringAsync());
        return Html(Layout("Could not save", "FuelStationERP · Web", auth, $"<section class='panel error'><h2>Could not save this record</h2><pre>{detail}</pre><a href='/branches/{branchId}/{resource}'>Go back</a></section>"));
    }
    return Results.Redirect($"/branches/{branchId}/{resource}");
}).RequireAuthorization();

app.MapGet("/branches/{branchId:long}/reports", async (long branchId, DateOnly? from, DateOnly? to, string? category, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    var start = from ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-29)); var end = to ?? DateOnly.FromDateTime(DateTime.Today);
    using var response = await ApiGet(clients, auth, $"api/v1/branches/{branchId}/reports/operations?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}");
    if (!response.IsSuccessStatusCode) return Html(Layout("Report Center", "FuelStationERP · Web", auth, Error(response)));
    var data = JsonNode.Parse(await response.Content.ReadAsStringAsync()); var allRows = ReportRows(data?["rows"] as JsonArray ?? []);
    var selectedCategory = ReportCategories().Contains(category ?? "Summary") ? category ?? "Summary" : "Summary";
    var rows = SelectReportCategory(allRows, selectedCategory); var totals = ReportCategoryTotals(rows);
    var q = $"?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}&category={Uri.EscapeDataString(selectedCategory)}";
    var categoryOptions = string.Concat(ReportCategories().Select(x => $"<option {(x == selectedCategory ? "selected" : "")}>{E(x)}</option>"));
    var tabs = string.Concat(ReportCategories().Select(x => $"<a class='export-button {(x == selectedCategory ? "" : "quiet-link")}' href='/branches/{branchId}/reports?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}&category={Uri.EscapeDataString(x)}'>{E(x)}</a>"));
    var body = $"<section class='hero'><span class='eyebrow'>CLEON · REPORTING</span><h1>Report Center</h1><p>Choose a category, filter the period and export the same report to PDF, Excel or CSV.</p></section>{BranchNav(branchId, "reports")}<section class='panel'><form method='get' class='form-grid'><label>From<input name='from' type='date' value='{start:yyyy-MM-dd}' required></label><label>To<input name='to' type='date' value='{end:yyyy-MM-dd}' required></label><label>Report category<select name='category'>{categoryOptions}</select></label><button>Refresh report</button></form><div class='export-actions'><a class='export-button' href='/branches/{branchId}/reports/export/pdf{q}'>Download PDF</a><a class='export-button' href='/branches/{branchId}/reports/export/excel{q}'>Download Excel</a><a class='export-button quiet-link' href='/branches/{branchId}/reports/export/csv{q}'>Download CSV</a></div><div class='export-actions'>{tabs}</div></section><section class='grid metrics'>{Metric("Sales", Money(totals, "sales"))}{Metric("Cost of goods sold", Money(totals, "costOfGoodsSold"))}{Metric("Gross profit", Money(totals, "grossProfit"))}{Metric("Expenses", Money(totals, "expenses"))}{Metric("Net operating profit", Money(totals, "netOperatingProfit"))}{Metric("Short / over", Money(totals, "shortOver"))}</section><section class='panel'><h2>{E(selectedCategory)} · {rows.Count} records</h2>{RenderReportRows(ReportJsonRows(rows))}</section>";
    return Html(Layout("Report Center", "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapGet("/branches/{branchId:long}/reports/export/{format}", async (long branchId, string format, DateOnly? from, DateOnly? to, string? category, HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    if (format is not ("pdf" or "excel" or "csv")) return Results.NotFound();
    var start = from ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-29)); var end = to ?? DateOnly.FromDateTime(DateTime.Today);
    using var response = await ApiGet(clients, auth, $"api/v1/branches/{branchId}/reports/operations?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}");
    if (!response.IsSuccessStatusCode) return Html(Layout("Report export", "FuelStationERP · Web", auth, Error(response)));
    var data = JsonNode.Parse(await response.Content.ReadAsStringAsync()); var allRows = ReportRows(data?["rows"] as JsonArray ?? []); var selectedCategory = ReportCategories().Contains(category ?? "Summary") ? category ?? "Summary" : "Summary";
    var rows = SelectReportCategory(allRows, selectedCategory); var totals = ReportCategoryTotals(rows);
    var filename = $"CLEON-{selectedCategory.Replace(' ', '-').Replace('/', '-')}-{start:yyyyMMdd}-{end:yyyyMMdd}";
    return format switch
    {
        "csv" => Results.File(Encoding.UTF8.GetBytes(ReportCsv(rows, totals, start, end, branchId)), "text/csv; charset=utf-8", filename + ".csv"),
        "excel" => Results.File(ReportXlsx(rows, totals, start, end, branchId), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename + ".xlsx"),
        _ => Results.File(ReportPdf(rows, totals, start, end, branchId), "application/pdf", filename + ".pdf")
    };
}).RequireAuthorization();

app.MapGet("/admin/users", async (HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    using var response = await ApiGet(clients, auth, "api/v1/users");
    if (!response.IsSuccessStatusCode) return Html(Layout("User administration", "FuelStationERP · Web", auth, Error(response)));
    var users = JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsArray() ?? [];
    var body = "<section class='hero'><span class='eyebrow'>ADMINISTRATION</span><h1>Team access</h1><p>Create a staff login and assign the branch and role they need.</p></section><section class='panel'><h2>Users</h2>" + RenderRecords(users) + "</section><section class='panel'><h2>Invite team member</h2><form method='post' action='/admin/users' class='form-grid'><label>Username<input name='username' required minlength='3'></label><label>Email<input name='email' type='email' required></label><label>Temporary password<input name='password' type='password' minlength='12' required></label><label>Branch ID<input name='branchId' type='number' min='1' required></label><label>Role<select name='role'><option>STAFF</option><option>MANAGER</option><option>ADMIN</option></select></label><button>Create user</button></form></section>";
    return Html(Layout("User administration", "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapPost("/admin/users", async (HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    var form = await context.Request.ReadFormAsync();
    var data = new { username = form["username"].ToString(), email = form["email"].ToString(), password = form["password"].ToString(), branchId = long.Parse(form["branchId"]!), role = form["role"].ToString() };
    using var response = await clients.CreateClient("erp").SendAsync(Authorized(new HttpRequestMessage(HttpMethod.Post, "api/v1/users") { Content = JsonContent.Create(data) }, auth));
    return response.IsSuccessStatusCode ? Results.Redirect("/admin/users") : Html(Layout("User administration", "FuelStationERP · Web", auth, $"<section class='panel error'><h2>Could not create user</h2><pre>{E(await response.Content.ReadAsStringAsync())}</pre></section>"));
}).RequireAuthorization();

app.MapGet("/admin/branches", async (HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login");
    using var response = await ApiGet(clients, auth, "api/v1/branches");
    var rows = response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync()) : null;
    var body = $"<section class='hero'><span class='eyebrow'>ADMINISTRATION</span><h1>Branches</h1><p>Manage the stations connected to this ERP.</p></section>{(response.IsSuccessStatusCode ? RenderRecords(rows) : Error(response))}<section class='panel'><h2>Add branch</h2><form method='post' class='form-grid'><label>Name<input name='name' required></label><label>Code<input name='code' required maxlength='20'></label><button>Create branch</button></form></section>";
    return Html(Layout("Branches", "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapPost("/admin/branches", async (HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login"); var form = await context.Request.ReadFormAsync();
    using var response = await Send(clients, auth, new HttpRequestMessage(HttpMethod.Post, "api/v1/branches") { Content = JsonContent.Create(new { name = form["name"].ToString(), code = form["code"].ToString() }) });
    return response.IsSuccessStatusCode ? Results.Redirect("/admin/branches") : Html(Layout("Branches", "FuelStationERP · Web", auth, $"<section class='panel error'><pre>{E(await response.Content.ReadAsStringAsync())}</pre></section>"));
}).RequireAuthorization();

app.MapGet("/admin/products", async (HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login"); using var response = await ApiGet(clients, auth, "api/v1/products");
    var rows = response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync()) : null;
    var body = $"<section class='hero'><span class='eyebrow'>ADMINISTRATION</span><h1>Products</h1><p>Configure the products sold and stored at your stations.</p></section>{(response.IsSuccessStatusCode ? RenderRecords(rows) : Error(response))}<section class='panel'><h2>Add product</h2><form method='post' class='form-grid'><label>Code<input name='code' required></label><label>Name<input name='name' required></label><label>Unit<input name='unitOfMeasure' value='L' required></label><label>Metered product<select name='isMetered'><option value='true'>Yes · pump meter</option><option value='false'>No · weighed or counted sale</option></select></label><button>Create product</button></form></section>";
    return Html(Layout("Products", "FuelStationERP · Web", auth, body));
}).RequireAuthorization();

app.MapPost("/admin/products", async (HttpContext context, IHttpClientFactory clients) =>
{
    var auth = Auth(context); if (auth is null) return Results.Redirect("/login"); var form = await context.Request.ReadFormAsync(); var isMetered = bool.TryParse(form["isMetered"], out var metered) && metered;
    using var response = await Send(clients, auth, new HttpRequestMessage(HttpMethod.Post, "api/v1/products") { Content = JsonContent.Create(new { code = form["code"].ToString(), name = form["name"].ToString(), unitOfMeasure = form["unitOfMeasure"].ToString(), isMetered }) });
    return response.IsSuccessStatusCode ? Results.Redirect("/admin/products") : Html(Layout("Products", "FuelStationERP · Web", auth, $"<section class='panel error'><pre>{E(await response.Content.ReadAsStringAsync())}</pre></section>"));
}).RequireAuthorization();

app.MapGet("/admin", () => Results.Redirect("/admin/users")).RequireAuthorization();
app.Run();

static ClaimsPrincipal? Auth(HttpContext context) => context.User.Identity?.IsAuthenticated == true ? context.User : null;
static HttpRequestMessage Authorized(HttpRequestMessage request, ClaimsPrincipal user) { var token = user.FindFirstValue("erp_jwt"); if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
static Task<HttpResponseMessage> ApiGet(IHttpClientFactory clients, ClaimsPrincipal user, string path) => clients.CreateClient("erp").SendAsync(Authorized(new HttpRequestMessage(HttpMethod.Get, path), user));
static Task<HttpResponseMessage> Send(IHttpClientFactory clients, ClaimsPrincipal user, HttpRequestMessage request) => clients.CreateClient("erp").SendAsync(Authorized(request, user));
static IResult Html(string value) => Results.Content(value, "text/html; charset=utf-8");
static string Val(JsonNode? node, string key) => node?[key]?.ToString() ?? "";
static string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
static string Metric(string title, string value) => $"<article class='metric'><span>{E(title)}</span><strong>{E(value)}</strong></article>";
static string Money(JsonNode? node, string key) => decimal.TryParse(node?[key]?.ToString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? $"₦{value:N2}" : "₦0.00";
static string BranchNav(long branchId, string active)
{
    var links = new[] { ("dashboard", "Overview"), ("pump-sessions", "Pump sessions"), ("fuel-receipts", "Deliveries"), ("product-sales", "LPG fills"), ("tank-dips", "Stock checks"), ("expenses", "Expenses"), ("accounts", "Accounts"), ("reports", "Reports"), ("tanks", "Tanks"), ("pumps", "Pumps"), ("attendants", "Attendants"), ("suppliers", "Suppliers"), ("product-prices", "Prices") };
    return "<nav class='branch-nav' aria-label='Branch operations'>" + string.Concat(links.Select(x => $"<a href='/branches/{branchId}{(x.Item1 == "dashboard" ? "" : "/" + x.Item1)}' {(x.Item1 == active ? "aria-current='page'" : "")}>{E(x.Item2)}</a>")) + "</nav>";
}
static string RenderRecentSessions(JsonArray? rows)
{
    if (rows is null || rows.Count == 0) return "<p class='empty'>No pump sessions have been posted today.</p>";
    var html = "<div class='table-wrap'><table><thead><tr><th>Session</th><th>Pump</th><th>Quantity</th><th>Sales</th><th>Short / over</th></tr></thead><tbody>";
    foreach (var row in rows)
    {
        var sales = decimal.TryParse(Val(row, "saleAmount"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var sale) ? $"₦{sale:N2}" : "₦0.00";
        var shortOver = decimal.TryParse(Val(row, "shortOver"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var gap) ? $"₦{gap:N2}" : "₦0.00";
        html += $"<tr><td>#{E(Val(row, "id"))}</td><td>Pump {E(Val(row, "pumpId"))}</td><td>{E(Val(row, "litresSold"))} L</td><td>{E(sales)}</td><td>{E(shortOver)}</td></tr>";
    }
    return html + "</tbody></table></div>";
}
static IEnumerable<Claim> JwtClaims(string token)
{
    var claims = new List<Claim>();
    try
    {
        var parts = token.Split('.');
        using var json = JsonDocument.Parse(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/').PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=')));
        foreach (var prop in json.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array) claims.AddRange(prop.Value.EnumerateArray().Select(item => new Claim(prop.Name, item.ToString())));
            else claims.Add(new Claim(prop.Name, prop.Value.ToString()));
        }
    }
    catch { }
    return claims;
}
static string LoginPage(string error = "") => Layout("Sign in", "FuelStationERP · Web", null, $"<main class='login'><img class='login-logo' src='/images/cleon_logo_dark.png' alt='CLEON Oil & Gas'><span class='eyebrow'>STATION OPERATIONS</span><h1>Welcome back</h1><p>Sign in to manage station activity.</p>{(error.Length == 0 ? "" : $"<p class='error'>{E(error)}</p>")}<form method='post' action='/login' class='form-grid'><label>Username<input name='username' autocomplete='username' required></label><label>Password<input name='password' type='password' autocomplete='current-password' required></label><button>Sign in</button></form></main>");
static string Layout(string title, string brand, ClaimsPrincipal? user, string body)
{
    var overviewCurrent = title.Contains("dashboard", StringComparison.OrdinalIgnoreCase) ? "aria-current='page'" : "";
    var branchesCurrent = title == "Branches" ? "aria-current='page'" : "";
    var productsCurrent = title == "Products" ? "aria-current='page'" : "";
    var teamCurrent = title == "User administration" ? "aria-current='page'" : "";
    var nav = user is null ? "" : $"<a class='skip-link' href='#main-content'>Skip to content</a><header class='app-header'><a class='brand' href='/'><img src='/images/cleon_logo_dark.png' alt='CLEON Oil & Gas'><span>STATION ERP</span></a><nav class='primary-nav' aria-label='Main navigation'><a href='/' {overviewCurrent}>Overview</a><a href='/admin/branches' {branchesCurrent}>Branches</a><a href='/admin/products' {productsCurrent}>Products</a><a href='/admin/users' {teamCurrent}>Team</a></nav><form method='post' action='/logout' class='user-actions'><span class='user-name'>{E(user.Identity?.Name)}</span><button class='quiet'>Sign out</button></form></header>";
    return $"<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><meta name='theme-color' content='#12392f'><title>{E(title)} · FuelStationERP</title><link rel='stylesheet' href='/site.css'></head><body>{nav}<main id='main-content' class='page'>{body}</main><footer>FuelStationERP · Station operations</footer></body></html>";
}
static string Error(HttpResponseMessage response) => $"<section class='panel error'><strong>API returned {(int)response.StatusCode} {E(response.ReasonPhrase)}</strong><p>{E(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())}</p></section>";
static string RenderRecords(JsonNode? node)
{
    var rows = node as JsonArray ?? new JsonArray(node?.DeepClone()); if (rows.Count == 0 || rows[0] is null) return "<p class='empty'>No records yet.</p>";
    var first = rows[0]!.AsObject(); var keys = first.Select(x => x.Key).Where(k => k is not "rowVersion" and not "payloadJson").Take(9).ToArray();
    var html = "<div class='table-wrap'><table><thead><tr>" + string.Concat(keys.Select(k => $"<th>{E(k)}</th>")) + "</tr></thead><tbody>";
    foreach (var row in rows) { if (row is null) continue; html += "<tr>" + string.Concat(keys.Select(k => $"<td>{E(row[k]?.ToString())}</td>")) + "</tr>"; }
    return html + "</tbody></table></div>";
}

static string RenderReportRows(JsonArray rows)
{
    if (rows.Count == 0) return "<p class='empty'>No activity was recorded for this date range.</p>";
    var keys = new[] { "type", "date", "product", "quantity", "unit", "sales", "costOfGoodsSold", "grossProfit", "shortOver" };
    var headers = new[] { "Type", "Date", "Product / category", "Quantity", "Unit", "Sales / expense", "Cost of goods sold", "Gross profit", "Short / over" };
    var html = "<div class='table-wrap'><table><thead><tr>" + string.Concat(headers.Select(h => $"<th>{h}</th>")) + "</tr></thead><tbody>";
    foreach (var row in rows) html += "<tr>" + string.Concat(keys.Select((key, i) => { var value = row?[key]?.ToString() ?? ""; if (i >= 5 && decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)) value = $"₦{amount:N2}"; return $"<td>{E(value)}</td>"; })) + "</tr>";
    return html + "</tbody></table></div>";
}

static string RenderReceiptRows(JsonNode? node)
{
    var rows = node as JsonArray ?? [];
    if (rows.Count == 0) return "<p class='empty'>No deliveries recorded yet.</p>";
    var columns = new (string Key, string Label)[] { ("id", "Receipt"), ("receiptDate", "Date"), ("productCodeSnapshot", "Product"), ("supplierNameSnapshot", "Supplier"), ("invoiceNumber", "Invoice"), ("invoiceLitres", "Invoice quantity"), ("receivedLitres", "Received quantity"), ("fuelShortage", "Fuel shortage"), ("unitOfMeasureSnapshot", "Unit"), ("unitCost", "Cost / unit") };
    var html = "<div class='table-wrap'><table><thead><tr>" + string.Concat(columns.Select(x => $"<th>{E(x.Label)}</th>")) + "</tr></thead><tbody>";
    foreach (var row in rows)
        html += "<tr>" + string.Concat(columns.Select(x => $"<td>{E(row?[x.Key]?.ToString())}</td>")) + "</tr>";
    return html + "</tbody></table></div>";
}

static string ReceiptShortForm(long branchId, JsonObject receipt)
{
    var id = E(Val(receipt, "id")); var shortage = E(Val(receipt, "fuelShortage"));
    return $"<section class='panel'><h2>Reconcile shortage · receipt {id}</h2><p>Outstanding invoice shortage before prior reconciliations: {shortage} {E(Val(receipt, "unitOfMeasureSnapshot"))}.</p><form method='post' action='/branches/{branchId}/fuel-receipts/{id}/short-reconciliation' class='form-grid'><label>Responsible party<select name='partyType' required><option>Supplier</option><option>Driver</option></select></label><label>Party name<input name='partyName' required></label><label>Settled quantity<input name='reconciledLitres' type='number' min='0' step='0.01' value='0'></label><label>Settled cost (NGN)<input name='reconciledCost' type='number' min='0' step='0.01' value='0'></label><label>Station-borne quantity<input name='stationBorneLitres' type='number' min='0' step='0.01' value='0'></label><label>Station-borne cost (NGN)<input name='stationBorneCost' type='number' min='0' step='0.01' value='0'></label><label>Settlement date<input name='settlementDate' type='date' value='{DateTime.Today:yyyy-MM-dd}' required></label><label><span>Disputed</span><input name='disputed' type='checkbox'></label><button>Save reconciliation</button></form></section>";
}

static string[] ReportCategories() => ["Summary", "Product / Delivery", "Profit & Loss", "Sales", "Tank Stock", "Fuel Receipts", "Attendants", "Expenses", "RTT", "Documents"];
static List<ReportItem> SelectReportCategory(List<ReportItem> rows, string category) => category switch
{
    "Summary" => rows.Where(x => x.Type != "Attendant activity").ToList(),
    "Product / Delivery" => rows.Where(x => x.Type is "Fuel delivery" or "Pump session" or "Product sale").ToList(),
    "Profit & Loss" => rows.Where(x => x.Type is "Pump session" or "Product sale" or "Expense").ToList(),
    "Sales" => rows.Where(x => x.Type is "Pump session" or "Product sale").ToList(),
    "Tank Stock" => rows.Where(x => x.Type is "Opening baseline" or "Tank stock check").ToList(),
    "Fuel Receipts" => rows.Where(x => x.Type == "Fuel delivery").ToList(),
    "Attendants" => rows.Where(x => x.Type == "Attendant activity").ToList(),
    "Expenses" => rows.Where(x => x.Type == "Expense").ToList(),
    "RTT" => rows.Where(x => x.Type == "RTT returned").ToList(),
    _ => []
};
static JsonObject ReportCategoryTotals(List<ReportItem> rows)
{
    var salesRows = rows.Where(x => x.Type is "Pump session" or "Product sale" or "Attendant activity").ToList();
    var sales = salesRows.Sum(x => x.Sales); var cogs = salesRows.Sum(x => x.Cogs); var gross = salesRows.Sum(x => x.GrossProfit);
    var expenses = -rows.Where(x => x.Type == "Expense").Sum(x => x.Sales);
    return new JsonObject { ["sales"] = sales, ["costOfGoodsSold"] = cogs, ["grossProfit"] = gross, ["expenses"] = expenses, ["netOperatingProfit"] = gross - expenses, ["shortOver"] = salesRows.Sum(x => x.ShortOver) };
}
static JsonArray ReportJsonRows(List<ReportItem> rows) => new(rows.Select(x => (JsonNode?)new JsonObject { ["type"] = x.Type, ["date"] = x.Date.ToString("yyyy-MM-dd"), ["product"] = x.Product, ["quantity"] = x.Quantity, ["unit"] = x.Unit, ["sales"] = x.Sales, ["costOfGoodsSold"] = x.Cogs, ["grossProfit"] = x.GrossProfit, ["shortOver"] = x.ShortOver }).ToArray());
static List<ReportItem> ReportRows(JsonArray rows) => rows.Select(x => new ReportItem(x?["type"]?.ToString() ?? "", DateOnly.TryParse(x?["date"]?.ToString(), out var date) ? date : default, x?["product"]?.ToString() ?? "", Dec(x?["quantity"]), x?["unit"]?.ToString() ?? "", Dec(x?["sales"]), Dec(x?["costOfGoodsSold"]), Dec(x?["grossProfit"]), Dec(x?["shortOver"]))).ToList();
static decimal Dec(JsonNode? node) => decimal.TryParse(node?.ToString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
static string Csv(object? value) { var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""; return "\"" + text.Replace("\"", "\"\"") + "\""; }
static string ReportCsv(List<ReportItem> rows, JsonNode? totals, DateOnly from, DateOnly to, long branch)
{
    var headers = new[] { "Type", "Date", "Product / category", "Quantity", "Unit", "Sales / expense (NGN)", "Cost of goods sold (NGN)", "Gross profit (NGN)", "Short / over (NGN)" };
    var lines = new List<string> { "CLEON FuelStationERP Report Center", $"Branch,{branch}", $"Period,{from:yyyy-MM-dd} to {to:yyyy-MM-dd}", "", string.Join(",", headers.Select(Csv)) };
    lines.AddRange(rows.Select(r => string.Join(",", new object?[] { r.Type, r.Date.ToString("yyyy-MM-dd"), r.Product, r.Quantity, r.Unit, r.Sales, r.Cogs, r.GrossProfit, r.ShortOver }.Select(Csv))));
    lines.Add(""); lines.Add($"Totals,,,,,{Dec(totals?["sales"])},{Dec(totals?["costOfGoodsSold"])},{Dec(totals?["grossProfit"])},{Dec(totals?["shortOver"])}"); lines.Add($"Expenses,,,,,{Dec(totals?["expenses"])}"); lines.Add($"Net operating profit,,,,,{Dec(totals?["netOperatingProfit"])}");
    return "\uFEFF" + string.Join("\r\n", lines);
}

static byte[] ReportXlsx(List<ReportItem> rows, JsonNode? totals, DateOnly from, DateOnly to, long branch)
{
    XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"; XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    var headers = new[] { "Type", "Date", "Product / category", "Quantity", "Unit", "Sales / expense (NGN)", "Cost of goods sold (NGN)", "Gross profit (NGN)", "Short / over (NGN)" };
    var matrix = new List<object?[]> { new object?[] { "CLEON · FuelStationERP Report Center" }, new object?[] { $"Branch {branch} · {from:yyyy-MM-dd} to {to:yyyy-MM-dd}" }, Array.Empty<object?>(), headers.Cast<object?>().ToArray() };
    matrix.AddRange(rows.Select(r => new object?[] { r.Type, r.Date.ToString("yyyy-MM-dd"), r.Product, r.Quantity, r.Unit, r.Sales, r.Cogs, r.GrossProfit, r.ShortOver }));
    matrix.Add(Array.Empty<object?>()); matrix.Add(new object?[] { "TOTALS", "", "", "", "", Dec(totals?["sales"]), Dec(totals?["costOfGoodsSold"]), Dec(totals?["grossProfit"]), Dec(totals?["shortOver"]) });
    matrix.Add(new object?[] { "Expenses", "", "", "", "", Dec(totals?["expenses"]) }); matrix.Add(new object?[] { "Net operating profit", "", "", "", "", Dec(totals?["netOperatingProfit"]) });
    var sheet = new XElement(m + "sheetData");
    for (var r = 0; r < matrix.Count; r++)
    {
        var cells = matrix[r].Select((value, c) => value is decimal or int or long
            ? new XElement(m + "c", new XAttribute("r", CellRef(c + 1, r + 1)), new XElement(m + "v", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)))
            : new XElement(m + "c", new XAttribute("r", CellRef(c + 1, r + 1)), new XAttribute("t", "inlineStr"), new XElement(m + "is", new XElement(m + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""))));
        sheet.Add(new XElement(m + "row", new XAttribute("r", r + 1), cells));
    }
    using var output = new MemoryStream(); using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
    {
        PutXml(zip, "[Content_Types].xml", XDocument.Parse("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>"""));
        PutXml(zip, "_rels/.rels", XDocument.Parse("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>"""));
        PutXml(zip, "xl/workbook.xml", new XDocument(new XElement(m + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(m + "sheets", new XElement(m + "sheet", new XAttribute("name", "Operations Report"), new XAttribute("sheetId", 1), new XAttribute(rel + "id", "rId1"))))));
        PutXml(zip, "xl/_rels/workbook.xml.rels", XDocument.Parse("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>"""));
        PutXml(zip, "xl/worksheets/sheet1.xml", new XDocument(new XElement(m + "worksheet", sheet)));
    }
    return output.ToArray();
}
static void PutXml(ZipArchive zip, string path, XDocument doc) { using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open(); doc.Save(stream); }
static string CellRef(int col, int row) { var name = ""; while (col > 0) { col--; name = (char)('A' + col % 26) + name; col /= 26; } return name + row; }

static byte[] ReportPdf(List<ReportItem> rows, JsonNode? totals, DateOnly from, DateOnly to, long branch)
{
    var lines = new List<string> { "CLEON  |  FUEL STATION ERP  |  REPORT CENTER", $"Branch {branch}  |  {from:yyyy-MM-dd} to {to:yyyy-MM-dd}", $"Sales NGN {Dec(totals?["sales"]):N2}    COGS NGN {Dec(totals?["costOfGoodsSold"]):N2}    Gross profit NGN {Dec(totals?["grossProfit"]):N2}", $"Expenses NGN {Dec(totals?["expenses"]):N2}    Net operating profit NGN {Dec(totals?["netOperatingProfit"]):N2}    Short / over NGN {Dec(totals?["shortOver"]):N2}", "TYPE        DATE        PRODUCT / CATEGORY      QTY    UNIT       SALES      COGS      GROSS PROFIT    SHORT/OVER" };
    lines.AddRange(rows.Select(r => $"{r.Type,-11} {r.Date:yyyy-MM-dd}  {r.Product,-21} {r.Quantity,8:N2} {r.Unit,-4} {r.Sales,10:N2} {r.Cogs,10:N2} {r.GrossProfit,12:N2} {r.ShortOver,10:N2}"));
    const int perPage = 37; var pageCount = Math.Max(1, (int)Math.Ceiling(lines.Count / (double)perPage)); var objs = new List<byte[]> { Array.Empty<byte>() };
    objs.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>")); objs.Add(Encoding.ASCII.GetBytes("<< /Type /Pages /Count " + pageCount + " /Kids [" + string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{5 + 2 * i} 0 R")) + "] >>")); objs.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"));
    for (var p = 0; p < pageCount; p++) { var content = new StringBuilder("BT /F1 7 Tf 28 565 Td 9 TL\n"); foreach (var line in lines.Skip(p * perPage).Take(perPage)) content.Append('(').Append(PdfEscape(line)).AppendLine(") Tj T* "); content.Append("ET"); var bytes = Encoding.ASCII.GetBytes(content.ToString()); var cid = 6 + 2 * p; objs.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R >> >> /Contents {cid} 0 R >>")); objs.Add(Encoding.ASCII.GetBytes($"<< /Length {bytes.Length} >>\nstream\n" + content + "\nendstream")); }
    using var pdf = new MemoryStream(); pdf.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n")); var offsets = new long[objs.Count]; for (var id = 1; id < objs.Count; id++) { offsets[id] = pdf.Position; pdf.Write(Encoding.ASCII.GetBytes($"{id} 0 obj\n")); pdf.Write(objs[id]); pdf.Write(Encoding.ASCII.GetBytes("\nendobj\n")); }
    var xref = pdf.Position; pdf.Write(Encoding.ASCII.GetBytes($"xref\n0 {objs.Count}\n0000000000 65535 f \n")); for (var id = 1; id < offsets.Length; id++) pdf.Write(Encoding.ASCII.GetBytes(offsets[id].ToString("0000000000", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n")); pdf.Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {objs.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF")); return pdf.ToArray();
}
static string PdfEscape(string s) => new string(s.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
static async Task<Dictionary<string, string>> LoadFormDataAsync(string resource, long branchId, ClaimsPrincipal auth, IHttpClientFactory clients)
{
    var result = new Dictionary<string, string>();
    async Task<JsonArray> GetRows(string path) { using var res = await ApiGet(clients, auth, path); return res.IsSuccessStatusCode ? JsonNode.Parse(await res.Content.ReadAsStringAsync()) as JsonArray ?? [] : []; }
    if (resource == "account-transactions")
    {
        var accounts = await GetRows($"api/v1/branches/{branchId}/accounts");
        result["accounts"] = SelectOptions(accounts, x => Val(x, "id"), x => $"{Val(x, "name")} · {Val(x, "accountType")} (₦{Val(x, "balance")})");
        var receipts = await GetRows($"api/v1/branches/{branchId}/fuel-receipts");
        result["receipts"] = SelectOptions(receipts, x => Val(x, "id"), x => $"Receipt #{Val(x, "id")} · {Val(x, "productCodeSnapshot")} · {Val(x, "supplierNameSnapshot")} · ₦{Val(x, "totalCost")}", true);
    }
    if (resource == "expenses")
    {
        var accounts = await GetRows($"api/v1/branches/{branchId}/accounts");
        result["accounts"] = SelectOptions(accounts, x => Val(x, "id"), x => $"{Val(x, "name")} · {Val(x, "accountType")}", true);
    }
    if (resource is "tanks" or "pump-sessions" or "product-sales" or "product-prices")
    {
        var products = await GetRows("api/v1/products");
        result["products"] = SelectOptions(products, x => x?["id"]?.ToString() ?? "", x => $"{Val(x, "code")} · {Val(x, "name")} ({Val(x, "unitOfMeasure")})");
    }
    if (resource is "pumps" or "fuel-receipts" or "tank-dips" or "product-sales" or "pump-sessions")
    {
        var tanks = await GetRows($"api/v1/branches/{branchId}/tanks");
        var products = await GetRows("api/v1/products");
        var names = products.ToDictionary(x => Val(x, "id"), x => Val(x, "code"));
        var metered = products.Where(x => bool.TryParse(Val(x, "isMetered"), out var yes) && yes).Select(x => Val(x, "id")).ToHashSet();
        var availableTanks = resource == "product-sales" ? new JsonArray(tanks.Where(x => !metered.Contains(Val(x, "productId"))).Select(x => x?.DeepClone()).ToArray()) : tanks;
        result["tanks"] = SelectOptions(availableTanks, x => Val(x, "id"), x => $"Tank {Val(x, "number")} · {names.GetValueOrDefault(Val(x, "productId"), "Product")}");
    }
    if (resource is "fuel-receipts")
    {
        var suppliers = await GetRows($"api/v1/branches/{branchId}/suppliers"); result["suppliers"] = SelectOptions(suppliers, x => Val(x, "id"), x => Val(x, "name"), true);
    }
    if (resource is "pump-sessions" or "product-sales")
    {
        var attendants = await GetRows($"api/v1/branches/{branchId}/attendants"); result["attendants"] = SelectOptions(attendants, x => Val(x, "id"), x => Val(x, "name"), true);
    }
    if (resource is "pump-runs" or "pump-sessions")
    {
        var pumps = await GetRows($"api/v1/branches/{branchId}/pumps");
        if (resource == "pump-sessions")
        {
            var tanks = await GetRows($"api/v1/branches/{branchId}/tanks"); var products = await GetRows("api/v1/products");
            var metered = products.Where(x => bool.TryParse(Val(x, "isMetered"), out var yes) && yes).Select(x => Val(x, "id")).ToHashSet();
            var tankProducts = tanks.ToDictionary(x => Val(x, "id"), x => Val(x, "productId"));
            pumps = new JsonArray(pumps.Where(x => tankProducts.TryGetValue(Val(x, "tankId"), out var productId) && metered.Contains(productId)).Select(x => x?.DeepClone()).ToArray());
        }
        result["pumps"] = SelectOptions(pumps, x => Val(x, "id"), x => $"Pump {Val(x, "number")}");
    }
    if (resource is "pump-sessions")
    {
        var runs = await GetRows($"api/v1/branches/{branchId}/pump-runs?status=OPEN"); result["runs"] = SelectOptions(runs, x => Val(x, "id"), x => $"Run {Val(x, "id")} · Pump ID {Val(x, "pumpId")}");
    }
    return result;
}
static string SelectOptions(JsonArray rows, Func<JsonNode?, string> id, Func<JsonNode?, string> label, bool optional = false) =>
    $"<option value=''>{(optional ? "None" : "Choose...")}</option>" + string.Concat(rows.Select(x => $"<option value='{E(id(x))}'>{E(label(x))}</option>"));

static string FormFor(string resource, long branchId, Dictionary<string, string> options)
{
    var (title, fields) = resource switch
    {
        "accounts" => ("Create named account", "<label>Account name<input name='name' minlength='2' required></label><label>Type<select name='accountType' required><option>CASH</option><option>BANK</option><option>POS</option></select></label><label>Account number<input name='accountNumber'></label><label>Bank / POS provider<input name='provider'></label><label>Opening balance (NGN)<input name='openingBalance' type='number' min='0' step='0.01' value='0'></label>"),
        "account-transactions" => ("Account ledger · cash vault · delivery payments", $"<label>Transaction type<select name='transactionType' required><option>Opening Balance</option><option>Account Receipt</option><option>Cash Count</option><option>Cash Expense</option><option>POS Transaction</option><option>Account Expense</option><option>Product Payment</option><option>Account Payment / Amount Sent</option><option>Bank Deposit</option><option>POS Settlement</option><option>Account Transfer</option></select></label><label>From / account<select name='accountId' required>{options.GetValueOrDefault("accounts")}</select></label><label>To account for transfers<select name='targetAccountId'>{options.GetValueOrDefault("accounts")}</select></label><label>Unpaid delivery for product payment<select name='fuelReceiptId'>{options.GetValueOrDefault("receipts")}</select></label><label>Amount (NGN)<input name='amount' type='number' min='0' step='0.01' required></label><label>Date<input name='transactionDate' type='date' value='{DateTime.Today:yyyy-MM-dd}' required></label><label>Reference / beneficiary<input name='reference'></label><label>Description<input name='description'></label>"),
        "product-prices" => ("Set selling price", $"<label>Product<select name='productId' required>{options.GetValueOrDefault("products")}</select></label><label>Selling price per unit (NGN)<input name='unitPrice' type='number' min='0.01' step='0.01' required></label><label>Effective date<input name='effectiveDate' type='date' required></label>"),
        "tanks" => ("Add tank", $"<label>Tank number<input name='number' required></label><label>Product<select name='productId' required>{options.GetValueOrDefault("products")}</select></label><label>Working capacity (L or kg)<input name='capacityLitres' type='number' min='0.01' step='0.01' required></label>"),
        "pumps" => ("Add pump", $"<label>Pump number<input name='number' required></label><label>Assigned tank<select name='tankId' required>{options.GetValueOrDefault("tanks")}</select></label>"),
        "attendants" => ("Add attendant", "<label>Full name<input name='name' required></label><label>Phone<input name='phone' autocomplete='tel'></label><label>Date joined<input name='dateJoined' type='date'></label><label>Monthly salary (NGN)<input name='monthlySalary' type='number' min='0' step='0.01' value='0'></label><label>Salary payday (1–31)<input name='salaryPaydayDay' type='number' min='1' max='31'></label>"),
        "suppliers" => ("Add supplier", "<label>Name<input name='name' required></label><label>Phone<input name='phone'></label>"),
        "fuel-receipts" => ("Record fuel delivery", $"<label>Tank<select name='tankId' required>{options.GetValueOrDefault("tanks")}</select></label><label>Supplier<select name='supplierId'>{options.GetValueOrDefault("suppliers")}</select></label><label>Invoice number<input name='invoiceNumber'></label><label>Invoice quantity<input name='invoiceLitres' type='number' min='0' step='0.01'></label><label>Received quantity<input name='receivedLitres' type='number' min='0.01' step='0.01' required></label><label>Cost per unit<input name='unitCost' type='number' min='0' step='0.01' required></label><label>Delivery date<input name='receiptDate' type='date' required></label><label>Vehicle<input name='vehicleNumber'></label><label>Driver<input name='driverName'></label>"),
        "expenses" => ("Record expense", $"<label>Description<input name='description' required></label><label>Category<input name='category'></label><label>Amount<input name='amount' type='number' step='0.01' required></label><label>Date<input name='expenseAt' type='datetime-local' value='{DateTime.Now:yyyy-MM-ddTHH:mm}' required></label><label>Paid from<select name='sourceAccountId'>{options.GetValueOrDefault("accounts")}</select></label><label>Notes<input name='notes'></label>"),
        "tank-dips" => ("Record tank reading", $"<label>Tank<select name='tankId' required>{options.GetValueOrDefault("tanks")}</select></label><label>Opening quantity<input name='openingDip' type='number' min='0' step='0.01' value='0'></label><label>Physical quantity<input name='closingDip' type='number' min='0' step='0.01' required></label>"),
        "pump-runs" => ("Open pump run", $"<label>Pump<select name='pumpId' required>{options.GetValueOrDefault("pumps")}</select></label><label>Opening meter<input name='openingMeter' type='number' min='0' step='0.01' required></label>"),
        "pump-sessions" => ("Post pump session", $"<label>Pump<select name='pumpId' required>{options.GetValueOrDefault("pumps")}</select></label><label>Attendant<select name='attendantId' required>{options.GetValueOrDefault("attendants")}</select></label><label>Open pump run<select name='runId' required>{options.GetValueOrDefault("runs")}</select></label><label>Opening meter<input name='openingMeter' type='number' min='0' step='0.01' required></label><label>Closing meter<input name='closingMeter' type='number' min='0' step='0.01' required></label><label>Price per unit<input name='pricePerLitre' type='number' min='0' step='0.01' required></label><label>Cash<input name='cashCollected' type='number' min='0' step='0.01' required></label><label>POS<input name='posAmount' type='number' min='0' step='0.01' required></label><label>Session expense<input name='expenses' type='number' min='0' step='0.01' value='0'></label><label>RTT quantity<input name='rttLitres' type='number' min='0' step='0.01' value='0'></label><label>Session date<input name='sessionDate' type='date' required></label>"),
        "product-sales" => ("Record product fill", $"<label>Non-metered product tank<select name='tankId' required>{options.GetValueOrDefault("tanks")}</select></label><label>Attendant<select name='attendantId'>{options.GetValueOrDefault("attendants")}</select></label><label>Quantity<input name='quantity' type='number' min='0.0001' step='0.01' required></label><label>Price per unit<input name='unitPrice' type='number' min='0' step='0.01' required></label><label>Cash<input name='cashCollected' type='number' min='0' step='0.01' required></label><label>POS<input name='posAmount' type='number' min='0' step='0.01' value='0'></label><label>Expense<input name='expenses' type='number' min='0' step='0.01' value='0'></label><label>Cylinder count<input name='cylinderCount' type='number' min='1'></label><label>Cylinder size<input name='cylinderSize'></label><label>Sale date<input name='saleDate' type='date' required></label>"),
        _ => ("", "")
    };
    if (fields.Length == 0) return "";
    if (resource == "pump-runs") return $"<section class='panel'><h2>{title}</h2><form method='post' action='/branches/{branchId}/pump-runs' class='form-grid'>{fields}<button>Open run</button></form></section>";
    if (resource is "accounts" or "account-transactions" or "fuel-receipts" or "expenses" or "tank-dips" or "tanks" or "pumps" or "attendants" or "suppliers" or "pump-sessions" or "product-sales" or "product-prices") return $"<section class='panel'><h2>{title}</h2><form method='post' action='/branches/{branchId}/{resource}' class='form-grid'>{fields}<button>Save record</button></form></section>";
    return "";
}
static object? FormPayload(string resource, IFormCollection f)
{
    string S(string key) => f[key].ToString(); long L(string key) => long.TryParse(S(key), out var n) ? n : 0; decimal D(string key) => decimal.TryParse(S(key), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0m;
    return resource switch
    {
        "product-prices" => new { productId = L("productId"), unitPrice = D("unitPrice"), effectiveDate = S("effectiveDate") },
        "accounts" => new { name = S("name"), accountType = S("accountType"), accountNumber = S("accountNumber"), provider = S("provider"), openingBalance = D("openingBalance") },
        "account-transactions" => new { transactionType = S("transactionType"), amount = D("amount"), accountId = L("accountId"), targetAccountId = L("targetAccountId") is var target && target > 0 ? (long?)target : null, fuelReceiptId = L("fuelReceiptId") is var receipt && receipt > 0 ? (long?)receipt : null, reference = S("reference"), description = S("description"), transactionDate = S("transactionDate") },
        "tanks" => new { number = S("number"), productId = L("productId"), capacityLitres = D("capacityLitres") },
        "pumps" => new { number = S("number"), tankId = L("tankId") },
        "attendants" => new { name = S("name"), phone = S("phone"), dateJoined = string.IsNullOrWhiteSpace(S("dateJoined")) ? (string?)null : S("dateJoined"), monthlySalary = D("monthlySalary"), salaryPaydayDay = L("salaryPaydayDay") is var payday && payday > 0 ? (int?)payday : null },
        "suppliers" => new { name = S("name"), phone = S("phone") },
        "fuel-receipts" => new { tankId = L("tankId"), supplierId = L("supplierId") is var supplier && supplier > 0 ? supplier : (long?)null, invoiceNumber = S("invoiceNumber"), invoiceLitres = D("invoiceLitres"), receivedLitres = D("receivedLitres"), unitCost = D("unitCost"), receiptDate = S("receiptDate"), vehicleNumber = S("vehicleNumber"), driverName = S("driverName") },
        "expenses" => new { description = S("description"), category = S("category"), amount = D("amount"), expenseAt = S("expenseAt"), sourceAccountId = L("sourceAccountId") is var account && account > 0 ? (long?)account : null, notes = S("notes") },
        "tank-dips" => new { tankId = L("tankId"), openingDip = D("openingDip"), closingDip = D("closingDip") },
        "pump-runs" => new { pumpId = L("pumpId"), openingMeter = D("openingMeter") },
        "pump-sessions" => new { pumpId = L("pumpId"), clientOperationId = Guid.NewGuid().ToString("N"), attendantId = L("attendantId"), runId = L("runId"), openingMeter = D("openingMeter"), closingMeter = D("closingMeter"), pricePerLitre = D("pricePerLitre"), cashCollected = D("cashCollected"), posAmount = D("posAmount"), expenses = D("expenses"), rttLitres = D("rttLitres"), sessionDate = S("sessionDate") },
        "product-sales" => new { tankId = L("tankId"), attendantId = L("attendantId") is var attendant && attendant > 0 ? attendant : (long?)null, quantity = D("quantity"), unitPrice = D("unitPrice"), cashCollected = D("cashCollected"), posAmount = D("posAmount"), expenses = D("expenses"), cylinderCount = L("cylinderCount") is var count && count > 0 ? (int?)count : null, cylinderSize = S("cylinderSize"), saleDate = S("saleDate") },
        _ => null
    };
}

sealed record ReportItem(string Type, DateOnly Date, string Product, decimal Quantity, string Unit, decimal Sales, decimal Cogs, decimal GrossProfit, decimal ShortOver);
