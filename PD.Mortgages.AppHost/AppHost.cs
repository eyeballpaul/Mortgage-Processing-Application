var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("mortgages-sql")
    .WithDataVolume("mortgages-sql-data");
var mortgagesDatabase = sqlServer.AddDatabase("mortgages");
var productsDatabase = sqlServer.AddDatabase("products");
var documentStorageRootPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PD.Mortgages");

var productsApi = builder.AddDotnetProject("products-api", "../PD.Products.Api/PD.Products.Api.csproj")
    .WithReference(productsDatabase)
    .WithEnvironment("Persistence__Provider", "SqlServer")
    .WaitFor(productsDatabase)
    .WithHttpHealthCheck("/health/ready");

builder.AddDotnetProject("mortgages-web", "../PD.Mortgages.Web/PD.Mortgages.Web.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithReference(mortgagesDatabase)
    .WithEnvironment("ProductService__UseMock", "false")
    .WithEnvironment("ProductService__BaseUrl", productsApi.GetEndpoint("https"))
    .WithEnvironment("Persistence__Provider", "SqlServer")
    .WithEnvironment("Persistence__RootPath", documentStorageRootPath)
    .WaitFor(mortgagesDatabase)
    .WaitFor(productsApi);

builder.AddDotnetProject("mortgages-staff-web", "../PD.Mortgages.Staff.Web/PD.Mortgages.Staff.Web.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithReference(mortgagesDatabase)
    .WithEnvironment("ProductService__UseMock", "false")
    .WithEnvironment("ProductService__BaseUrl", productsApi.GetEndpoint("https"))
    .WithEnvironment("Persistence__Provider", "SqlServer")
    .WithEnvironment("Persistence__RootPath", documentStorageRootPath)
    .WaitFor(mortgagesDatabase)
    .WaitFor(productsApi);

builder.Build().Run();
