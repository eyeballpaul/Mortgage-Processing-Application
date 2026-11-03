namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class MortgagePersistenceOptions
{
    public const string SectionName = "Persistence";

    public string Provider { get; set; } = "InMemory";
    public string RootPath { get; set; } = "data";
}
