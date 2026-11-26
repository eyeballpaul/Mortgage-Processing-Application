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

    [Fact]
    public async Task AnonymousSessionsCannotReadOrChangeEachOthersDrafts()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var owner = CreateService(repository);
        var other = CreateService(repository, new OtherApplicationActor());
        var application = await owner.CreateAsync();
        await owner.SaveQualificationAsync(application.Id, 300_000m, 30_000m);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.GetAsync(application.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.SaveQualificationAsync(application.Id, 400_000m, 40_000m));
        Assert.Equal(300_000m, (await owner.GetAsync(application.Id)).PropertyValue);
    }

    [Fact]
    public async Task SubmittedApplicationIsNotAvailableToTheCustomerSession()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository);
        var application = await service.CreateAsync();
        await service.SaveQualificationAsync(application.Id, 300_000m, 30_000m);
        await service.SaveApplicantAsync(application.Id, "Jane", "Doe", "jane@example.com", "07123456789");
        await service.SaveFinancesAsync(application.Id, 100_000m, 1_000m, EmploymentType.Employed);
        await service.SaveConsentsAsync(application.Id, false, false);
        await service.GenerateDecisionInPrincipleAsync(application.Id);
        var products = await service.GetEligibleProductsAsync(application.Id);
        Assert.NotEmpty(products);
        await service.SelectProductAsync(application.Id, products[0].Id);
        await service.SubmitAsync(application.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(application.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteAsync(application.Id));
        var staff = CreateService(repository, new StaffApplicationActor());
        Assert.Equal(ApplicationStatus.Submitted, (await staff.GetForStaffAsync(application.Id)).Status);
    }

    [Fact]
    public async Task MissingSessionIdentityCannotCreateOrAccessAnApplication()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var application = await CreateService(repository).CreateAsync();
        var service = CreateService(repository, new MissingApplicationActor());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(application.Id));
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
            Options.Create(new MortgageWorkflowOptions { ConsentTermsVersion = "terms-2026-01" }));

    private sealed class TestApplicationActor : IApplicationActor
    {
        public string? SubjectId => "customer-1";
        public bool IsAuthenticated => false;
        public bool IsStaff => false;
    }

    private sealed class OtherApplicationActor : IApplicationActor
    {
        public string? SubjectId => "customer-2";
        public bool IsAuthenticated => false;
        public bool IsStaff => false;
    }

    private sealed class MissingApplicationActor : IApplicationActor
    {
        public string? SubjectId => null;
        public bool IsAuthenticated => false;
        public bool IsStaff => false;
    }

    private sealed class StaffApplicationActor : IApplicationActor
    {
        public string SubjectId => "staff-1";
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
    }
}
