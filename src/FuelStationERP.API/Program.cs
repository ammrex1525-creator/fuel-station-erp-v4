using System.Text;
using FuelStationERP.Infrastructure.Persistence;
using FuelStationERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var databasePath = builder.Configuration["Database:Path"];
if (string.IsNullOrWhiteSpace(databasePath))
{
    var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelStationERP");
    Directory.CreateDirectory(dataDirectory);
    databasePath = Path.Combine(dataDirectory, "station-v4.db");
}
var sharedConnection = builder.Configuration.GetConnectionString("Erp");
builder.Services.AddDbContext<ErpDbContext>(o =>
{
    if (!string.IsNullOrWhiteSpace(sharedConnection)) o.UseSqlServer(sharedConnection);
    else o.UseSqlite($"Data Source={databasePath}");
});
builder.Services.AddScoped<FifoInventoryService>();
builder.Services.AddScoped<ProductSaleInventoryService>();
builder.Services.AddIdentityCore<ApplicationUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 12;
    o.Password.RequireDigit = true;
    o.Password.RequireNonAlphanumeric = true;
    o.Lockout.AllowedForNewUsers = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ErpDbContext>()
    .AddSignInManager();

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Set Jwt:SigningKey to a secret with at least 32 UTF-8 bytes (use environment variable Jwt__SigningKey or a secret store).");
var issuer = builder.Configuration["Jwt:Issuer"] ?? "FuelStationERP";
var audience = builder.Configuration["Jwt:Audience"] ?? "FuelStationERP.Clients";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true, ValidIssuer = issuer,
    ValidateAudience = true, ValidAudience = audience,
    ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
});
builder.Services.AddAuthorization();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (db.Database.IsSqlite())
    {
        var dipsConnection = db.Database.GetDbConnection();
        if (dipsConnection.State != System.Data.ConnectionState.Open) await dipsConnection.OpenAsync();
        await using (var check = dipsConnection.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(\"TankDips\")";
            await using var reader = await check.ExecuteReaderAsync(); var hasBaselineCost = false;
            while (await reader.ReadAsync()) if (string.Equals(reader.GetString(1), "BaselineCost", StringComparison.OrdinalIgnoreCase)) hasBaselineCost = true;
            await reader.DisposeAsync();
            if (!hasBaselineCost) { await using var alter = dipsConnection.CreateCommand(); alter.CommandText = "ALTER TABLE TankDips ADD COLUMN BaselineCost TEXT NULL"; await alter.ExecuteNonQueryAsync(); }
        }
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ProductPrices (Id INTEGER NOT NULL CONSTRAINT PK_ProductPrices PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL, UnitPrice TEXT NOT NULL, EffectiveDate TEXT NOT NULL);");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductPrices_ProductId_EffectiveDate ON ProductPrices (ProductId, EffectiveDate);");
    }
    else
    {
        await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH(N'dbo.TankDips', N'BaselineCost') IS NULL ALTER TABLE [TankDips] ADD [BaselineCost] decimal(19,4) NULL;");
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'[ProductPrices]', N'U') IS NULL CREATE TABLE [ProductPrices] ([Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ProductPrices] PRIMARY KEY, [ProductId] bigint NOT NULL, [UnitPrice] decimal(19,4) NOT NULL, [EffectiveDate] date NOT NULL);");
        await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ProductPrices_ProductId_EffectiveDate' AND object_id=OBJECT_ID(N'[ProductPrices]')) CREATE UNIQUE INDEX [IX_ProductPrices_ProductId_EffectiveDate] ON [ProductPrices] ([ProductId], [EffectiveDate]);");
    }
    if (!await db.Branches.AnyAsync())
    {
        db.Branches.Add(new FuelStationERP.Domain.Entities.Branch { Name = "Main Branch", Code = "MAIN" });
        await db.SaveChangesAsync();
    }
    if (!await db.Products.AnyAsync())
    {
        db.Products.AddRange(
            new FuelStationERP.Domain.Entities.FuelProduct { Code = "PMS", Name = "Premium Motor Spirit", UnitOfMeasure = "L", IsMetered = true },
            new FuelStationERP.Domain.Entities.FuelProduct { Code = "AGO", Name = "Automotive Gas Oil", UnitOfMeasure = "L", IsMetered = true },
            new FuelStationERP.Domain.Entities.FuelProduct { Code = "LPG", Name = "Liquefied Petroleum Gas", UnitOfMeasure = "kg", IsMetered = false });
        await db.SaveChangesAsync();
    }

    var bootstrapName = builder.Configuration["Bootstrap:Username"];
    var bootstrapEmail = builder.Configuration["Bootstrap:Email"];
    var bootstrapPassword = builder.Configuration["Bootstrap:Password"];
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "OWNER", "ADMIN", "MANAGER", "STAFF" })
        if (!await roleManager.RoleExistsAsync(role)) await roleManager.CreateAsync(new IdentityRole(role));
    if (!await userManager.Users.AnyAsync() && !string.IsNullOrWhiteSpace(bootstrapName) &&
        !string.IsNullOrWhiteSpace(bootstrapEmail) && !string.IsNullOrWhiteSpace(bootstrapPassword))
    {
        var owner = new ApplicationUser { UserName = bootstrapName, Email = bootstrapEmail, EmailConfirmed = true };
        var created = await userManager.CreateAsync(owner, bootstrapPassword);
        if (!created.Succeeded) throw new InvalidOperationException("Could not create the configured first owner account: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(owner, "OWNER");
    }
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "FuelStationERP.API" })).AllowAnonymous();
app.MapControllers();
app.Run();
