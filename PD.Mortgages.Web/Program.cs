using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(30);
    options.Cookie.Name = ".PDMortgages.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = TimeSpan.FromDays(30);
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddSingleton<IMortgageApplicationRepository, InMemoryMortgageApplicationRepository>();
builder.Services.AddSingleton<IMortgageDocumentStorage, InMemoryMortgageDocumentStorage>();
var useMockProductService = bool.TryParse(builder.Configuration["ProductService:UseMock"], out var configuredUseMock)
    && configuredUseMock;

if (useMockProductService)
{
    builder.Services.AddSingleton<IProductCatalogClient, MockProductCatalogClient>();
}
else
{
    builder.Services.AddHttpClient<IProductCatalogClient, ProductCatalogClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["ProductService:BaseUrl"] ?? "http://localhost:5260/");
        client.Timeout = TimeSpan.FromSeconds(10);
    });
}
builder.Services.AddSingleton<IMortgageApplicationService, MortgageApplicationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
