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
        var applicationRepository = new InMemoryMortgageApplicationRepository();
        var applicationService = new MortgageApplicationService(
            applicationRepository,
            applicationRepository.AuditRepository,
            new MockProductCatalogClient(new LocalProductCatalogFallback()),
            new InMemoryMortgageDocumentStorage(),
            new TestApplicationActor(),
            Options.Create(new ApplicationSecurityOptions { RequireAuthenticatedUser = true }),
            Options.Create(new MortgageWorkflowOptions { ConsentTermsVersion = "terms-2026-01" }));
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

    [Fact]
    public async Task ConsentHistoryIsAttributedAndPersistedWithTheApplication()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository);
        var application = await service.CreateAsync();

        await service.SaveConsentsAsync(application.Id, true, false);

        var persisted = await repository.GetByIdAsync(application.Id);
        Assert.NotNull(persisted);
        Assert.Equal(2, persisted.ConsentHistory.Count);
        Assert.All(persisted.ConsentHistory, consent =>
        {
            Assert.Equal("terms-2026-01", consent.TermsVersion);
            Assert.Equal("customer-1", consent.ActorId);
        });
    }

    [Fact]
    public async Task CustomerCannotDeleteAnotherOwnersApplication()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var ownerService = CreateService(repository);
        var application = await ownerService.CreateAsync();
        var otherActorService = CreateService(repository, new OtherApplicationActor());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherActorService.DeleteAsync(application.Id));
        Assert.NotNull(await repository.GetByIdAsync(application.Id));
    }

    private static MortgageApplicationService CreateService(
        InMemoryMortgageApplicationRepository repository,
        IApplicationActor? actor = null) =>
        new(
            repository,
            repository.AuditRepository,
            new MockProductCatalogClient(new LocalProductCatalogFallback()),
            new InMemoryMortgageDocumentStorage(),
            actor ?? new TestApplicationActor(),
            Options.Create(new ApplicationSecurityOptions { RequireAuthenticatedUser = true }),
            Options.Create(new MortgageWorkflowOptions { ConsentTermsVersion = "terms-2026-01" }));

    private sealed class TestApplicationActor : IApplicationActor
    {
        public string? SubjectId => "customer-1";
        public bool IsAuthenticated => true;
        public bool IsStaff => false;
    }

    private sealed class OtherApplicationActor : IApplicationActor
    {
        public string? SubjectId => "customer-2";
        public bool IsAuthenticated => true;
        public bool IsStaff => false;
    }
}
