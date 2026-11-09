var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddHttpClient("ErpApi", client => client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7001"));
var app = builder.Build();
app.UseHttpsRedirection(); app.UseStaticFiles(); app.MapRazorPages(); app.MapGet("/", () => Results.Redirect("/Index")); app.Run();
