namespace PD.Mortgages.Web.Configuration;

public static class ProductionConfigurationValidator
{
    public static void Validate(IHostEnvironment environment, IConfiguration configuration)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var failures = new List<string>();
        if (string.Equals(configuration["Session:Provider"], "Redis", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            failures.Add("ConnectionStrings:Redis is required when Session:Provider is Redis.");
        }
        if (string.Equals(configuration["Session:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("mortgages")))
        {
            failures.Add("ConnectionStrings:mortgages is required when Session:Provider is SqlServer.");
        }

        if (string.IsNullOrWhiteSpace(configuration["ProductService:BaseUrl"]))
        {
            failures.Add("ProductService:BaseUrl is required outside Development.");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", failures));
        }
    }
}
