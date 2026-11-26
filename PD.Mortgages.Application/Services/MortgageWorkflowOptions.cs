namespace PD.Mortgages.Application.Services;

public sealed class MortgageWorkflowOptions
{
    public const string SectionName = "MortgageWorkflow";

    public string ConsentTermsVersion { get; set; } = "1";
}
