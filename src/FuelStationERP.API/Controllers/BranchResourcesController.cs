using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/branches/{branchId:long}")]
public sealed class BranchResourcesController(ErpDbContext db) : ControllerBase
{
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(long branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.BranchId == branchId).OrderBy(x => x.AccountType).ThenBy(x => x.Name).ToListAsync(ct);
        var txns = await db.AccountTransactions.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        return Ok(accounts.Select(x => new { x.Id, x.Name, x.AccountType, x.AccountNumber, x.Provider, x.IsActive, Balance = AccountBalance(x, txns, today) }));
    }

    [HttpPost("accounts")]
    public async Task<IActionResult> CreateAccount(long branchId, [FromBody] CreateAccountRequest request, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        var name = request.Name.Trim(); var type = request.AccountType.Trim().ToUpperInvariant();
        if (name.Length < 2 || type is not ("CASH" or "BANK" or "POS") || request.OpeningBalance < 0) return BadRequest("Enter an account name, choose CASH/BANK/POS and use a non-negative opening balance.");
        if (await db.Accounts.AnyAsync(x => x.BranchId == branchId && x.Name == name && x.AccountType == type, ct)) return Conflict(new { error = "An account with this name and type already exists." });
        var account = new Account { BranchId = branchId, Name = name, AccountType = type, AccountNumber = request.AccountNumber, Provider = request.Provider };
        db.Accounts.Add(account); await db.SaveChangesAsync(ct);
        if (request.OpeningBalance > 0) db.AccountTransactions.Add(new AccountTransaction { BranchId = branchId, AccountId = account.Id, TransactionType = "Opening Balance", Amount = request.OpeningBalance, Reference = "Opening balance", OccurredAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/accounts", account);
    }

    [HttpGet("account-transactions")]
    public async Task<IActionResult> AccountTransactions(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.AccountTransactions.AsNoTracking().Where(x => x.BranchId == branchId).OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(500).ToListAsync(ct));

    [HttpPost("account-transactions")]
    public async Task<IActionResult> CreateAccountTransaction(long branchId, [FromBody] AccountTransactionRequest request, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == request.AccountId && x.BranchId == branchId && x.IsActive, ct);
        if (account is null) return BadRequest("Choose an active account in this branch.");
        var type = request.TransactionType.Trim();
        var allowed = new[] { "Opening Balance", "Account Receipt", "Cash Count", "Cash Expense", "POS Transaction", "Account Expense", "Product Payment", "Account Payment / Amount Sent", "Bank Deposit", "POS Settlement", "Account Transfer" };
        if (!allowed.Contains(type) || request.Amount < 0 || (request.Amount == 0 && type != "Cash Count")) return BadRequest("Choose a supported account transaction and enter a positive amount; cash counts may be zero.");
        if (type is "Cash Count" or "Cash Expense" && account.AccountType != "CASH") return BadRequest("Choose a cash vault account for cash count or cash expense.");
        if (type == "POS Transaction" && account.AccountType != "POS") return BadRequest("Choose a POS account for POS receipts.");
        if (type == "Account Receipt" && account.AccountType != "CASH") return BadRequest("Choose a cash vault for cash receipts.");
        if (type == "Account Expense" && account.AccountType == "CASH") return BadRequest("Use Cash Expense for cash vault outflows.");
        if (type is "Account Payment / Amount Sent" or "Product Payment" or "Account Expense" && string.IsNullOrWhiteSpace(request.Reference)) return BadRequest("Enter a beneficiary or payment reference.");
        var targetId = request.TargetAccountId;
        if (type is "Bank Deposit" or "POS Settlement" or "Account Transfer")
        {
            var target = targetId is long id ? await db.Accounts.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == branchId && x.IsActive, ct) : null;
            if (target is null || target.Id == account.Id) return BadRequest("Choose a different active destination account.");
            if (type == "Bank Deposit" && (account.AccountType != "CASH" || target.AccountType != "BANK")) return BadRequest("Bank deposits move funds from CASH to BANK.");
            if (type == "POS Settlement" && (account.AccountType != "POS" || target.AccountType != "BANK")) return BadRequest("POS settlements move funds from POS to BANK.");
        }
        else targetId = null;
        if (type == "Opening Balance" && await db.AccountTransactions.AnyAsync(x => x.BranchId == branchId && x.AccountId == account.Id && x.TransactionType == type, ct)) return BadRequest("An opening balance has already been recorded for this account.");
        long? receiptId = null;
        if (type == "Product Payment")
        {
            if (request.FuelReceiptId is not long selectedReceipt) return BadRequest("Choose the delivery receipt this payment covers.");
            var receipt = await db.FuelReceipts.SingleOrDefaultAsync(x => x.Id == selectedReceipt && x.BranchId == branchId, ct);
            if (receipt is null) return BadRequest("The selected delivery receipt was not found.");
            var stationCosts = await db.ReceiptShortSettlements.Where(x => x.BranchId == branchId && x.FuelReceiptId == selectedReceipt).SumAsync(x => (decimal?)x.StationBorneCost, ct) ?? 0;
            var paid = await db.AccountTransactions.Where(x => x.BranchId == branchId && x.FuelReceiptId == selectedReceipt && x.TransactionType == "Product Payment").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var due = Math.Max(receipt.TotalCost + stationCosts - paid, 0);
            if (request.Amount > due + 0.005m) return BadRequest($"Payment exceeds the receipt balance due of ₦{due:N2}.");
            receiptId = selectedReceipt;
        }
        var txns = await db.AccountTransactions.Where(x => x.BranchId == branchId).ToListAsync(ct);
        var outflow = type is "Cash Expense" or "Account Expense" or "Product Payment" or "Account Payment / Amount Sent" or "Bank Deposit" or "POS Settlement" or "Account Transfer";
        var current = AccountBalance(account, txns, request.TransactionDate);
        if (outflow && request.Amount > current + 0.005m) return BadRequest($"Amount exceeds the account balance of ₦{current:N2}.");
        var item = new AccountTransaction { BranchId = branchId, AccountId = account.Id, TargetAccountId = targetId, FuelReceiptId = receiptId, TransactionType = type,
            Amount = request.Amount, Reference = request.Reference, Description = request.Description, OccurredAt = new DateTimeOffset(request.TransactionDate.ToDateTime(TimeOnly.MinValue), DateTimeOffset.UtcNow.Offset) };
        db.AccountTransactions.Add(item); await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/account-transactions", item);
    }

    [HttpGet("account-reconciliation")]
    public async Task<IActionResult> AccountReconciliation(long branchId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var sessions = await db.PumpSessions.Where(x => x.BranchId == branchId && x.SessionDate == date).ToListAsync(ct);
        var ids = sessions.Select(x => x.Id).ToList();
        var collections = ids.Count == 0 ? [] : await db.CashCollections.Where(x => x.BranchId == branchId && ids.Contains(x.PumpSessionId)).ToListAsync(ct);
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), DateTimeOffset.UtcNow.Offset); var end = start.AddDays(1);
        var txns = await db.AccountTransactions.Where(x => x.BranchId == branchId && x.OccurredAt >= start && x.OccurredAt < end).ToListAsync(ct);
        var accounts = await db.Accounts.Where(x => x.BranchId == branchId).ToListAsync(ct);
        var types = accounts.ToDictionary(x => x.Id, x => x.AccountType);
        var expectedCash = sessions.Sum(x => x.CashCollected); var expectedPos = sessions.Sum(x => x.PosAmount);
        var cashCollected = collections.Count == 0 ? expectedCash : collections.Sum(x => x.CashAmount); var posCollected = collections.Count == 0 ? expectedPos : collections.Sum(x => x.PosAmount);
        var cashExpense = txns.Where(x => x.TransactionType == "Cash Expense" && x.AccountId is long id && types.GetValueOrDefault(id) == "CASH").Sum(x => x.Amount);
        var cashCount = txns.Where(x => x.TransactionType == "Cash Count" && x.AccountId is long id && types.GetValueOrDefault(id) == "CASH").OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Select(x => (decimal?)x.Amount).FirstOrDefault();
        var cashMoved = txns.Where(x => x.AccountId is long id && types.GetValueOrDefault(id) == "CASH" && ((x.TargetAccountId is long target && types.GetValueOrDefault(target) == "BANK" && x.TransactionType is "Bank Deposit" or "Account Transfer") || (x.TargetAccountId is null && x.TransactionType is "Account Payment / Amount Sent" or "Product Payment"))).Sum(x => x.Amount);
        var posEntered = txns.Where(x => x.AccountId is long id && types.GetValueOrDefault(id) == "POS" && x.TransactionType == "POS Transaction").Sum(x => x.Amount);
        var posMoved = txns.Where(x => x.AccountId is long id && types.GetValueOrDefault(id) == "POS" && (x.TransactionType is "POS Settlement" or "Account Transfer" or "Account Payment / Amount Sent" or "Product Payment" or "Account Expense")).Sum(x => x.Amount);
        var cashGap = cashCollected + posCollected - expectedCash - expectedPos;
        var movementGap = cashMoved + posMoved - (Math.Max(expectedCash - cashExpense, 0) + expectedPos);
        var status = Math.Abs(cashGap) <= 0.01m && Math.Abs(movementGap) <= 0.01m ? "BALANCED" : movementGap < -0.01m ? "PENDING / SHORT" : "OVER";
        return Ok(new { date, expectedCash, expectedPos, cashCollected, posCollected, cashCollectionGap = cashGap, cashVaultCount = cashCount, cashExpense, cashMoved, posEntered, posMoved, cashPending = Math.Max((cashCount ?? Math.Max(expectedCash - cashExpense, 0)) - cashMoved, 0), posPending = Math.Max((posEntered > 0 ? posEntered : expectedPos) - posMoved, 0), pumpShortOver = sessions.Sum(x => x.ShortOver), status });
    }

    [HttpGet("product-prices")]
    public async Task<IActionResult> ProductPrices(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await (from pp in db.ProductPrices.AsNoTracking() join p in db.Products on pp.ProductId equals p.Id orderby pp.EffectiveDate descending select new { pp.Id, pp.ProductId, Product = p.Code, Unit = p.UnitOfMeasure, pp.UnitPrice, pp.EffectiveDate }).ToListAsync(ct));

    [HttpPost("product-prices")]
    public async Task<IActionResult> SaveProductPrice(long branchId, [FromBody] ProductPrice item, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        if (item.UnitPrice <= 0 || item.EffectiveDate == default || !await db.Products.AnyAsync(x => x.Id == item.ProductId && x.IsActive, ct)) return BadRequest("Choose an active product, a positive selling price and an effective date.");
        var existing = await db.ProductPrices.SingleOrDefaultAsync(x => x.ProductId == item.ProductId && x.EffectiveDate == item.EffectiveDate, ct);
        if (existing is null) { item.Id = 0; db.ProductPrices.Add(item); }
        else existing.UnitPrice = item.UnitPrice;
        await db.SaveChangesAsync(ct);
        return Ok(existing ?? item);
    }

    [HttpGet("product-prices/effective")]
    public async Task<IActionResult> EffectiveProductPrice(long branchId, [FromQuery] long productId, [FromQuery] DateOnly onDate, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var price = await db.ProductPrices.Where(x => x.ProductId == productId && x.EffectiveDate <= onDate)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id).Select(x => new { x.UnitPrice, x.EffectiveDate }).FirstOrDefaultAsync(ct);
        return price is null ? NotFound(new { error = $"No selling price is effective for {onDate:yyyy-MM-dd}." }) : Ok(price);
    }

    [HttpGet("tanks")]
    public async Task<IActionResult> Tanks(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.Tanks.AsNoTracking().Where(x => x.BranchId == branchId).OrderBy(x => x.Number).ToListAsync(ct));
    [HttpPost("tanks")]
    public async Task<IActionResult> CreateTank(long branchId, [FromBody] Tank item, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        item.Number = item.Number.Trim();
        if (string.IsNullOrWhiteSpace(item.Number) || item.CapacityLitres <= 0 || !await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct) || !await db.Products.AnyAsync(x => x.Id == item.ProductId && x.IsActive, ct)) return BadRequest("Choose an active product, enter a tank number and set a positive tank capacity.");
        if (await db.Tanks.AnyAsync(x => x.BranchId == branchId && x.Number == item.Number, ct)) return Conflict(new { error = "A tank with this number already exists in the branch." });
        item.Id = 0; item.BranchId = branchId; db.Tanks.Add(item); await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/tanks", item);
    }

    [HttpGet("pumps")]
    public async Task<IActionResult> Pumps(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.Pumps.AsNoTracking().Where(x => x.BranchId == branchId).OrderBy(x => x.Number).ToListAsync(ct));
    [HttpPost("pumps")]
    public async Task<IActionResult> CreatePump(long branchId, [FromBody] Pump item, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        item.Number = item.Number.Trim();
        if (string.IsNullOrWhiteSpace(item.Number)) return BadRequest("Enter a pump number.");
        if (await db.Pumps.AnyAsync(x => x.BranchId == branchId && x.Number == item.Number, ct)) return Conflict(new { error = "A pump with this number already exists in the branch." });
        var tankProduct = await db.Tanks.Where(x => x.Id == item.TankId && x.BranchId == branchId && x.IsActive).Join(db.Products, tank => tank.ProductId, product => product.Id, (tank, product) => new { product.IsActive, product.IsMetered }).SingleOrDefaultAsync(ct);
        if (tankProduct is null || !tankProduct.IsActive || !tankProduct.IsMetered) return BadRequest("Choose an active metered product tank in this branch for a pump.");
        item.Id = 0; item.BranchId = branchId; db.Pumps.Add(item); await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/pumps", item);
    }

    [HttpGet("pump-runs")]
    public async Task<IActionResult> PumpRuns(long branchId, [FromQuery] string? status, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.PumpRuns.AsNoTracking().Where(x => x.BranchId == branchId && (status == null || x.Status == status)).OrderByDescending(x => x.Id).Take(100).ToListAsync(ct));
    [HttpPost("pump-runs")]
    public async Task<IActionResult> OpenPumpRun(long branchId, [FromBody] OpenPumpRunRequest request, CancellationToken ct)
    {
        if (!CanOperate(branchId)) return Forbid();
        var pump = await db.Pumps.SingleOrDefaultAsync(x => x.Id == request.PumpId && x.BranchId == branchId && x.IsActive, ct);
        if (pump is null) return BadRequest("Choose an active pump in this branch.");
        if (await db.PumpRuns.AnyAsync(x => x.BranchId == branchId && x.PumpId == pump.Id && x.Status == "OPEN", ct)) return Conflict(new { error = "This pump already has an open run." });
        var tank = await db.Tanks.SingleOrDefaultAsync(x => x.Id == pump.TankId && x.IsActive, ct);
        if (tank is null) return Conflict(new { error = "The pump has no active tank." });
        var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId, ct);
        if (!product.IsMetered) return BadRequest("Pump runs require a tank assigned to a metered product.");
        var run = new PumpRun { BranchId = branchId, PumpId = pump.Id, TankId = tank.Id, ProductCodeSnapshot = product.Code, OpeningMeter = request.OpeningMeter, StartedAt = DateTimeOffset.UtcNow, Status = "OPEN" };
        db.PumpRuns.Add(run);
        await db.SaveChangesAsync(ct);
        var batches = await db.FuelReceipts.Where(x => x.BranchId == branchId && x.ProductCodeSnapshot == product.Code && x.RemainingLitres > 0).ToListAsync(ct);
        db.PumpRunReceipts.AddRange(batches.Select(x => new PumpRunReceipt { PumpRunId = run.Id, FuelReceiptId = x.Id, QuantityReceived = x.ReceivedLitres, TotalCost = x.TotalCost }));
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/branches/{branchId}/pump-runs", run);
    }

    [HttpPost("pump-runs/{runId:long}/close")]
    public async Task<IActionResult> ClosePumpRun(long branchId, long runId, [FromBody] ClosePumpRunRequest request, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        var run = await db.PumpRuns.SingleOrDefaultAsync(x => x.Id == runId && x.BranchId == branchId && x.Status == "OPEN", ct);
        if (run is null) return NotFound();
        if (run.OpeningMeter is decimal opening && request.ClosingMeter < opening) return BadRequest("Closing meter cannot be below opening meter.");
        run.ClosingMeter = request.ClosingMeter; run.FinishedAt = DateTimeOffset.UtcNow; run.Status = "CLOSED";
        await db.SaveChangesAsync(ct); return Ok(run);
    }

    [HttpGet("suppliers")]
    public async Task<IActionResult> Suppliers(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.Suppliers.AsNoTracking().Where(x => x.BranchId == branchId).OrderBy(x => x.Name).ToListAsync(ct));
    [HttpPost("suppliers")]
    public async Task<IActionResult> CreateSupplier(long branchId, [FromBody] Supplier item, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        if (string.IsNullOrWhiteSpace(item.Name)) return BadRequest("Supplier name is required.");
        item.Id = 0; item.BranchId = branchId; item.Name = item.Name.Trim(); db.Suppliers.Add(item); await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/suppliers", item);
    }

    [HttpGet("attendants")]
    public async Task<IActionResult> Attendants(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.StaffAttendants.AsNoTracking().Where(x => x.BranchId == branchId).OrderBy(x => x.Name).ToListAsync(ct));
    [HttpPost("attendants")]
    public async Task<IActionResult> CreateAttendant(long branchId, [FromBody] StaffAttendant item, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        if (string.IsNullOrWhiteSpace(item.Name) || item.MonthlySalary < 0 || item.SalaryPaydayDay is < 1 or > 31 || item.DateJoined > DateOnly.FromDateTime(DateTime.Today)) return BadRequest("Enter a name, a non-negative salary, a valid joined date and a payday between 1 and 31 when provided.");
        item.Id = 0; item.BranchId = branchId; item.Name = item.Name.Trim(); item.Phone = string.IsNullOrWhiteSpace(item.Phone) ? null : item.Phone.Trim(); db.StaffAttendants.Add(item); await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/attendants", item);
    }

    [HttpGet("fuel-receipts")]
    public async Task<IActionResult> Receipts(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.FuelReceipts.AsNoTracking().Where(x => x.BranchId == branchId).OrderByDescending(x => x.ReceiptDate).ThenByDescending(x => x.Id).ToListAsync(ct));
    [HttpPost("fuel-receipts")]
    public async Task<IActionResult> CreateReceipt(long branchId, [FromBody] FuelReceipt item, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        var tank = await db.Tanks.SingleOrDefaultAsync(x => x.Id == item.TankId && x.BranchId == branchId && x.IsActive, ct);
        if (tank is null) return BadRequest("Choose an active tank in this branch.");
        var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId, ct);
        if (item.InvoiceLitres < 0 || item.ReceivedLitres <= 0 || item.UnitCost < 0 || item.ReceiptDate == default) return BadRequest("Enter a valid date, positive received quantity and non-negative invoice quantity and unit cost.");
        Supplier? supplier = null;
        if (item.SupplierId is long supplierId)
        {
            supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == supplierId && x.BranchId == branchId, ct);
            if (supplier is null) return BadRequest("Choose a supplier in this branch.");
        }
        item.Id = 0; item.BranchId = branchId; item.ProductCodeSnapshot = product.Code; item.UnitOfMeasureSnapshot = product.UnitOfMeasure;
        item.SupplierNameSnapshot = supplier?.Name;
        item.RemainingLitres = item.ReceivedLitres; item.EffectiveUnitCost ??= item.UnitCost; item.TotalCost = item.ReceivedLitres * item.UnitCost;
        db.FuelReceipts.Add(item); await db.SaveChangesAsync(ct);
        var activeRuns = await db.PumpRuns.Where(x => x.BranchId == branchId && x.TankId == tank.Id && x.Status == "OPEN").ToListAsync(ct);
        db.PumpRunReceipts.AddRange(activeRuns.Select(run => new PumpRunReceipt { PumpRunId = run.Id, FuelReceiptId = item.Id, QuantityReceived = item.ReceivedLitres, TotalCost = item.TotalCost }));
        await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/fuel-receipts", item);
    }

    [HttpPost("fuel-receipts/{receiptId:long}/short-reconciliation")]
    public async Task<IActionResult> ReconcileReceiptShort(long branchId, long receiptId, [FromBody] ReceiptShortSettlement item, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        var receipt = await db.FuelReceipts.SingleOrDefaultAsync(x => x.Id == receiptId && x.BranchId == branchId, ct);
        if (receipt is null) return NotFound("Fuel receipt was not found.");
        if (item.PartyType is not ("Supplier" or "Driver") || string.IsNullOrWhiteSpace(item.PartyName)) return BadRequest("Choose Supplier or Driver and enter their name.");
        if (item.ReconciledLitres < 0 || item.ReconciledCost < 0 || item.StationBorneLitres < 0 || item.StationBorneCost < 0 ||
            (item.ReconciledLitres + item.ReconciledCost + item.StationBorneLitres + item.StationBorneCost) <= 0)
            return BadRequest("Enter positive, non-negative settlement amounts.");
        var initialLitres = Math.Max(receipt.InvoiceLitres - receipt.ReceivedLitres, 0);
        if (initialLitres <= 0 || receipt.ReceivedLitres <= 0) return BadRequest("This receipt has no reconcilable invoice shortage.");
        var prior = await db.ReceiptShortSettlements.Where(x => x.BranchId == branchId && x.FuelReceiptId == receiptId).ToListAsync(ct);
        var remainingLitres = Math.Max(initialLitres - prior.Sum(x => x.ReconciledLitres + x.StationBorneLitres), 0);
        var remainingCost = Math.Max(initialLitres * receipt.UnitCost - prior.Sum(x => x.ReconciledCost + x.StationBorneCost), 0);
        if (item.ReconciledLitres + item.StationBorneLitres > remainingLitres + 0.005m || item.ReconciledCost + item.StationBorneCost > remainingCost + 0.005m)
            return BadRequest($"Settlement exceeds the outstanding shortage ({remainingLitres:N2} units / ₦{remainingCost:N2}).");
        item.Id = 0; item.BranchId = branchId; item.FuelReceiptId = receiptId;
        db.ReceiptShortSettlements.Add(item);
        var stationCost = prior.Sum(x => x.StationBorneCost) + item.StationBorneCost;
        receipt.EffectiveUnitCost = Math.Max(receipt.TotalCost + stationCost, 0) / receipt.ReceivedLitres;
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/branches/{branchId}/fuel-receipts/{receiptId}/short-reconciliation", item);
    }

    [HttpGet("expenses")]
    public async Task<IActionResult> Expenses(long branchId, CancellationToken ct) => !CanAccess(branchId) ? Forbid() : Ok(await db.Expenses.AsNoTracking().Where(x => x.BranchId == branchId).OrderByDescending(x => x.ExpenseAt).ToListAsync(ct));
    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense(long branchId, [FromBody] Expense item, CancellationToken ct)
    {
        if (!CanOperate(branchId)) return Forbid();
        if (string.IsNullOrWhiteSpace(item.Description) || item.Amount <= 0) return BadRequest("Enter an expense description and a positive amount.");
        Account? source = null;
        if (item.SourceAccountId is long accountId)
        {
            source = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId && x.BranchId == branchId && x.IsActive, ct);
            if (source is null) return BadRequest("Choose an active source account in this branch.");
            var balance = AccountBalance(source, await db.AccountTransactions.Where(x => x.BranchId == branchId).ToListAsync(ct), DateOnly.FromDateTime(item.ExpenseAt.LocalDateTime));
            if (item.Amount > balance + 0.005m) return BadRequest($"Expense exceeds the selected account balance of ₦{balance:N2}.");
        }
        item.Id = 0; item.BranchId = branchId; item.Description = item.Description.Trim(); db.Expenses.Add(item);
        if (source is not null) db.AccountTransactions.Add(new AccountTransaction { BranchId = branchId, AccountId = source.Id, TransactionType = source.AccountType == "CASH" ? "Cash Expense" : "Account Expense", Amount = item.Amount, Description = item.Description, OccurredAt = item.ExpenseAt, Reference = item.Category });
        await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/expenses", item);
    }

    [HttpGet("tank-dips")]
    public async Task<IActionResult> TankDips(long branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var dips = await db.TankDips.AsNoTracking().Where(x => x.BranchId == branchId).ToListAsync(ct);
        return Ok(dips.OrderByDescending(x => x.RecordedAt.UtcDateTime).ThenByDescending(x => x.Id).Take(100));
    }
    [HttpPost("tank-dips")]
    public async Task<IActionResult> CreateTankDip(long branchId, [FromBody] TankDip item, CancellationToken ct)
    {
        if (!CanOperate(branchId)) return Forbid();
        var tank = await db.Tanks.SingleOrDefaultAsync(x => x.Id == item.TankId && x.BranchId == branchId && x.IsActive, ct);
        if (tank is null) return BadRequest("Choose an active tank in this branch.");
        if (item.OpeningDip < 0 || item.ClosingDip < 0 || item.OpeningDip > tank.CapacityLitres || item.ClosingDip > tank.CapacityLitres) return BadRequest("Opening and closing readings must be within the tank capacity.");
        var history = await db.TankDips.Where(x => x.BranchId == branchId && x.TankId == tank.Id).ToListAsync(ct);
        var previous = history.OrderByDescending(x => x.RecordedAt.UtcDateTime).ThenByDescending(x => x.Id).FirstOrDefault();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        item.ExpectedLitres = item.ClosingDip;
        if (previous is null)
        {
            if (item.ClosingDip > 0)
            {
                var product = await db.Products.SingleAsync(x => x.Id == tank.ProductId, ct);
                var receipts = await db.FuelReceipts.Where(x => x.BranchId == branchId && x.ProductCodeSnapshot == product.Code && x.ReceiptDate <= today).ToListAsync(ct);
                var costedQuantity = receipts.Sum(x => x.ReceivedLitres);
                if (costedQuantity <= 0) return BadRequest("Record a costed opening receipt before saving a non-zero baseline, so its inventory cost can be calculated.");
                var receiptValue = receipts.Sum(x => x.ReceivedLitres * (x.EffectiveUnitCost ?? x.UnitCost));
                var weightedUnitCost = receiptValue / costedQuantity;
                if (weightedUnitCost <= 0) return BadRequest("The available receipts have no positive weighted cost to value this baseline.");
                item.BaselineCost = item.ClosingDip * weightedUnitCost;
            }
            item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? "Opening baseline" : $"{item.Notes.Trim()} · opening baseline";
        }
        else
        {
            var start = DateOnly.FromDateTime(previous.RecordedAt.UtcDateTime);
            var received = await db.FuelReceipts.Where(x => x.BranchId == branchId && x.TankId == tank.Id && x.ReceiptDate > start && x.ReceiptDate <= today).SumAsync(x => (decimal?)x.ReceivedLitres, ct) ?? 0;
            var pumpIds = await db.Pumps.Where(x => x.BranchId == branchId && x.TankId == tank.Id).Select(x => x.Id).ToListAsync(ct);
            var pumped = await db.PumpSessions.Where(x => x.BranchId == branchId && pumpIds.Contains(x.PumpId) && x.SessionDate > start && x.SessionDate <= today).SumAsync(x => (decimal?)x.LitresSold, ct) ?? 0;
            var otherSales = await db.ProductSales.Where(x => x.BranchId == branchId && x.TankId == tank.Id && x.SaleDate > start && x.SaleDate <= today).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
            item.ExpectedLitres = item.OpeningDip + received - pumped - otherSales;
            var variance = item.ExpectedLitres.Value - item.ClosingDip;
            item.Notes = $"{item.Notes?.Trim()} · opening {item.OpeningDip:N2}; expected {item.ExpectedLitres:N2}; variance {variance:+0.##;-0.##;0}";
        }
        item.Id = 0; item.BranchId = branchId; item.RecordedAt = DateTimeOffset.UtcNow; db.TankDips.Add(item); await db.SaveChangesAsync(ct); return Created($"/api/v1/branches/{branchId}/tank-dips", item);
    }

    private bool CanAccess(long branchId) => User.IsInRole("OWNER") || User.FindAll("branch_id").Any(x => x.Value == branchId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private bool CanManage(long branchId) => CanOperate(branchId) && (User.IsInRole("OWNER") || User.FindAll("branch_role").Any(x => x.Value == $"{branchId}:ADMIN" || x.Value == $"{branchId}:MANAGER"));
    private bool CanOperate(long branchId) => User.IsInRole("OWNER") || User.FindAll("branch_role").Any(x => x.Value == $"{branchId}:STAFF" || x.Value == $"{branchId}:MANAGER" || x.Value == $"{branchId}:ADMIN");

    private static decimal AccountBalance(Account account, IReadOnlyCollection<AccountTransaction> all, DateOnly cutoff)
    {
        var items = all.Where(x => DateOnly.FromDateTime(x.OccurredAt.LocalDateTime) <= cutoff && (x.AccountId == account.Id || x.TargetAccountId == account.Id)).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToList();
        var anchor = account.AccountType == "CASH" ? items.LastOrDefault(x => x.AccountId == account.Id && x.TransactionType == "Cash Count") : null;
        decimal balance = anchor?.Amount ?? 0;
        if (anchor is not null) items = items.Where(x => x.OccurredAt.Date > anchor.OccurredAt.Date || (x.OccurredAt.Date == anchor.OccurredAt.Date && x.Id > anchor.Id)).ToList();
        foreach (var item in items)
        {
            if (item.TransactionType == "Cash Count") continue;
            if (item.TargetAccountId == account.Id && item.AccountId != account.Id) { balance += item.Amount; continue; }
            if (item.AccountId != account.Id) continue;
            if (item.TransactionType is "Opening Balance" or "Account Receipt" or "POS Transaction" or "Cash collection" or "POS collection") balance += item.Amount;
            else if (item.TransactionType is "Account Payment / Amount Sent" or "Bank Payment / Withdrawal" or "Cash Expense" or "Account Expense" or "Product Payment" or "Expense") balance -= item.Amount;
            else if (item.TransactionType is "Account Transfer" or "Bank Deposit" or "POS Settlement")
            {
                if (item.TargetAccountId is long target && target != account.Id) balance -= item.Amount;
                else if (item.TargetAccountId is null) balance += item.Amount;
            }
        }
        return balance;
    }
}

public sealed record OpenPumpRunRequest(long PumpId, decimal OpeningMeter);
public sealed record ClosePumpRunRequest(decimal ClosingMeter);
public sealed record CreateAccountRequest(string Name, string AccountType, string? AccountNumber, string? Provider, decimal OpeningBalance = 0);
public sealed record AccountTransactionRequest(string TransactionType, decimal Amount, long AccountId, long? TargetAccountId, long? FuelReceiptId, string? Reference, string? Description, DateOnly TransactionDate);
