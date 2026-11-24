var builder = DistributedApplication.CreateBuilder(args);
var appHostDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
var sharedDataPath = Path.GetFullPath(Path.Combine(appHostDirectory, "..", "PD.Mortgages.Web", "data"));

var productsApi = builder.AddDotnetProject("products-api", "../PD.Products.Api/PD.Products.Api.csproj")
    .WithHttpHealthCheck("/health/ready");

builder.AddDotnetProject("mortgages-web", "../PD.Mortgages.Web/PD.Mortgages.Web.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ProductService__UseMock", "false")
    .WithEnvironment("ProductService__BaseUrl", productsApi.GetEndpoint("https"))
    .WithEnvironment("Persistence__RootPath", sharedDataPath)
    .WaitFor(productsApi);

builder.AddDotnetProject("mortgages-staff-web", "../PD.Mortgages.Staff.Web/PD.Mortgages.Staff.Web.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ProductService__UseMock", "false")
    .WithEnvironment("ProductService__BaseUrl", productsApi.GetEndpoint("https"))
    .WithEnvironment("Persistence__RootPath", sharedDataPath)
    .WaitFor(productsApi);

builder.Build().Run();
