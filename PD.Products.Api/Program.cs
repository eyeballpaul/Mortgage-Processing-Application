using PD.Products.Application.Services;
using PD.Products.Domain.Repositories;
using PD.Products.Domain.Services;
using PD.Products.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IMortgageProductRepository, InMemoryMortgageProductRepository>();
builder.Services.AddSingleton<ProductEligibilityService>();
builder.Services.AddSingleton<IProductService, ProductService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program { }
