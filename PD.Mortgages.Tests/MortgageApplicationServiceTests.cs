using Microsoft.Extensions.Options;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;

namespace PD.Mortgages.Tests;

public sealed class MortgageApplicationServiceTests
{
    [Fact]
    public async Task DecisionWithoutCreditConsentIsGeneratedWithoutTheSimulatedCheckDelay()
    {
        var applicationService = new MortgageApplicationService(
            new InMemoryMortgageApplicationRepository(),
            new InMemoryMortgageApplicationAuditRepository(),
            new MockProductCatalogClient(new LocalProductCatalogFallback()),
            new InMemoryMortgageDocumentStorage(),
            new TestApplicationActor(),
            Options.Create(new ApplicationSecurityOptions { RequireAuthenticatedUser = true }));
        var application = await applicationService.CreateAsync();
        await applicationService.SaveQualificationAsync(application.Id, 300_000m, 30_000m);
        await applicationService.SaveFinancesAsync(application.Id, 100_000m, 1_000m, EmploymentType.Employed);
        await applicationService.SaveConsentsAsync(application.Id, creditCheckConsent: false, openBankingConsent: false);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var result = await applicationService.GenerateDecisionInPrincipleAsync(application.Id, cancellation.Token);

        Assert.NotNull(result.DecisionInPrinciple);
        Assert.Equal(DecisionInPrincipleOutcome.CreditCheckNotSelected, result.DecisionInPrinciple.Outcome);
        Assert.Null(result.DecisionInPrinciple.IndicativeMaximumBorrowing);
    }

    private sealed class TestApplicationActor : IApplicationActor
    {
        public string? SubjectId => "customer-1";
        public bool IsAuthenticated => true;
        public bool IsStaff => false;
    }
}
