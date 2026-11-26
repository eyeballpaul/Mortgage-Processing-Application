using PD.Mortgages.Application.Services;
using PD.Mortgages.Client;
using PD.Mortgages.Staff.Web.Controllers;
using PD.Mortgages.Web.Controllers;

namespace PD.Mortgages.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void WebAndClientAssembliesDoNotReferenceMortgageImplementations()
    {
        var assemblies = new[]
        {
            typeof(HomeController).Assembly,
            typeof(ApplicationsController).Assembly,
            typeof(MortgageApiClient).Assembly,
            typeof(IMortgageApplicationService).Assembly,
            typeof(PD.Mortgages.Application.Models.MortgageContractMapper).Assembly
        };
        var forbidden = new[] { "PD.Mortgages.Application", "PD.Mortgages.Domain", "PD.Mortgages.Infrastructure" };

        foreach (var assembly in assemblies)
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => forbidden.Contains(reference.Name));
    }
}
