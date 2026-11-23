var builder = DistributedApplication.CreateBuilder(args);

var productsApi = builder.AddDotnetProject("products-api", "../PD.Products.Api/PD.Products.Api.csproj")
    .WithHttpHealthCheck("/health/ready");

builder.AddDotnetProject("mortgages-web", "../PD.Mortgages.Web/PD.Mortgages.Web.csproj")
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ProductService__UseMock", "false")
    .WithEnvironment("ProductService__BaseUrl", productsApi.GetEndpoint("https"))
    .WaitFor(productsApi);

builder.Build().Run();
