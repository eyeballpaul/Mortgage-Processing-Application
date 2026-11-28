using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Types.Enums;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;

namespace PD.Mortgages.Tests;

public sealed class MortgageApplicationServiceTests
{
    [Fact]
    public async Task SelectedProductSnapshotDoesNotChangeWhenCatalogChanges()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var catalog = new MutableCatalog();
        var service = CreateService(repository, catalog: catalog);
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);
        await service.SaveQualificationAsync(application.Id, 300_000, 30_000, application.Version, TestContext.Current.CancellationToken);
        var current = await service.GetAsync(application.Id, TestContext.Current.CancellationToken);
        await service.SelectProductAsync(application.Id, "fixed-2", current.Version, TestContext.Current.CancellationToken);
        catalog.Product = catalog.Product with { InterestRate = 9m, CatalogVersion = "changed" };

        var selected = (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).SelectedProduct;
        Assert.NotNull(selected);
        Assert.Equal(4m, selected.InterestRate);
        Assert.Equal("original", selected.CatalogVersion);
    }

    [Fact]
    public async Task ConcurrentUploadIsRejectedAndItsStoredContentIsRemoved()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var storage = new PausingDocumentStorage();
        var service = CreateService(repository, storage: storage);
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);
        var upload = service.AddDocumentAsync(application.Id, "test.pdf", "%PDF-1.7"u8.ToArray(), application.Version, TestContext.Current.CancellationToken);
        await storage.Stored.Task;
        await service.SaveQualificationAsync(application.Id, 300_000m, 30_000m, application.Version, TestContext.Current.CancellationToken);
        storage.Continue.SetResult();

        await Assert.ThrowsAsync<MortgageConcurrencyException>(() => upload);
        Assert.Null(await storage.GetAsync(application.Id, storage.DocumentId, TestContext.Current.CancellationToken));
        Assert.Empty((await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Documents);
    }

    [Fact]
    public async Task UploadContentIsRetainedWhenCommitSucceededButResponseWasLost()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var storage = new InMemoryMortgageDocumentStorage();
        var application = await CreateService(repository, storage: storage).CreateAsync(TestContext.Current.CancellationToken);
        var service = new MortgageApplicationService(
            new CommitResponseLostRepository(repository), repository.AuditRepository,
            new MockProductCatalogClient(new LocalProductCatalogFallback()), new TestEligibilityClient(), storage,
            new TestApplicationActor(), Options.Create(new MortgageWorkflowOptions()),
            new DocumentUploadValidator(), repository, NullLogger<MortgageApplicationService>.Instance);

        await Assert.ThrowsAsync<IOException>(() =>
            service.AddDocumentAsync(application.Id, "test.pdf", "%PDF-1.7"u8.ToArray(), application.Version, TestContext.Current.CancellationToken));

        var document = Assert.Single((await repository.GetByIdAsync(application.Id, TestContext.Current.CancellationToken))!.Documents);
        Assert.Equal("%PDF-1.7"u8.ToArray(), await storage.GetAsync(application.Id, document.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UploadContentIsRetainedWhenStorageResponseWasLost()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var storage = new UploadResponseLostStorage();
        var service = CreateService(repository, storage: storage);
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(() =>
            service.AddDocumentAsync(application.Id, "test.pdf", "%PDF-1.7"u8.ToArray(), application.Version, TestContext.Current.CancellationToken));

        Assert.Empty((await repository.GetByIdAsync(application.Id, TestContext.Current.CancellationToken))!.Documents);
        Assert.NotNull(await storage.GetAsync(application.Id, storage.DocumentId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DecisionRejectsAnEditMadeDuringItsCreditCheck()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository);
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);
        await service.SaveQualificationAsync(application.Id, 300_000m, 30_000m, application.Version, TestContext.Current.CancellationToken);
        await service.SaveFinancesAsync(application.Id, 100_000m, 1_000m, EmploymentType.Employed, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.SaveConsentsAsync(application.Id, true, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        var version = (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version;

        var decision = service.GenerateDecisionInPrincipleAsync(application.Id, version, TestContext.Current.CancellationToken);
        await service.SaveQualificationAsync(application.Id, 400_000m, 40_000m, version, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<MortgageConcurrencyException>(() => decision);
        Assert.Null((await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).DecisionInPrinciple);
    }

    [Fact]
    public async Task DecisionWithoutCreditConsentIsGeneratedWithoutTheSimulatedCheckDelay()
    {
        var applicationRepository = new InMemoryMortgageApplicationRepository();
        var applicationService = new MortgageApplicationService(
            applicationRepository,
            applicationRepository.AuditRepository,
            new MockProductCatalogClient(new LocalProductCatalogFallback()),
            new TestEligibilityClient(),
            new InMemoryMortgageDocumentStorage(),
            new TestApplicationActor(),
            Options.Create(new MortgageWorkflowOptions { ConsentTermsVersion = "terms-2026-01" }),
            new DocumentUploadValidator(),
            applicationRepository,
            NullLogger<MortgageApplicationService>.Instance);
        var application = await applicationService.CreateAsync(TestContext.Current.CancellationToken);
        await applicationService.SaveQualificationAsync(application.Id, 300_000m, 30_000m, application.Version, TestContext.Current.CancellationToken);
        await applicationService.SaveFinancesAsync(application.Id, 100_000m, 1_000m, EmploymentType.Employed, (await applicationService.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await applicationService.SaveConsentsAsync(application.Id, creditCheckConsent: false, (await applicationService.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, cancellationToken: TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var result = await applicationService.GenerateDecisionInPrincipleAsync(application.Id, (await applicationService.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, cancellation.Token);

        Assert.NotNull(result.DecisionInPrinciple);
        Assert.Equal(DecisionInPrincipleOutcome.CreditCheckNotSelected, result.DecisionInPrinciple.Outcome);
        Assert.Null(result.DecisionInPrinciple.IndicativeMaximumBorrowing);
        Assert.Equal("1", (await applicationRepository.GetByIdAsync(application.Id, TestContext.Current.CancellationToken))!.DecisionInPrinciple!.RuleVersion);
    }

    [Fact]
    public async Task EligibilityOutageDoesNotSaveDecisionOrAudit()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository, eligibility: new UnavailableEligibilityClient());
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);
        await service.SaveQualificationAsync(application.Id, 300_000, 30_000, application.Version, TestContext.Current.CancellationToken);
        var current = await service.GetAsync(application.Id, TestContext.Current.CancellationToken);
        await service.SaveFinancesAsync(application.Id, 100_000, 1_000, EmploymentType.Employed, current.Version, TestContext.Current.CancellationToken);
        current = await service.GetAsync(application.Id, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<EligibilityUnavailableException>(() => service.GenerateDecisionInPrincipleAsync(application.Id, current.Version, TestContext.Current.CancellationToken));
        Assert.Null((await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).DecisionInPrinciple);
        Assert.Equal(current.Version, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task ConsentHistoryIsAttributedAndPersistedWithTheApplication()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository);
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);

        await service.SaveConsentsAsync(application.Id, true, application.Version, TestContext.Current.CancellationToken);

        var persisted = await repository.GetByIdAsync(application.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
        Assert.Single(persisted.ConsentHistory);
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
        var application = await ownerService.CreateAsync(TestContext.Current.CancellationToken);
        var otherActorService = CreateService(repository, new OtherApplicationActor());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherActorService.DeleteAsync(application.Id, application.Version, TestContext.Current.CancellationToken));
        Assert.NotNull(await repository.GetByIdAsync(application.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingDraftRequiresCurrentVersionAndQueuesCleanupWithAudit()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var storage = new InMemoryMortgageDocumentStorage();
        var service = CreateService(repository, storage: storage);
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);
        var documentId = await service.AddDocumentAsync(application.Id, "test.pdf", "%PDF-1.7"u8.ToArray(), application.Version, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<MortgageConcurrencyException>(() => service.DeleteAsync(application.Id, application.Version, TestContext.Current.CancellationToken));
        Assert.NotNull(await storage.GetAsync(application.Id, documentId, TestContext.Current.CancellationToken));
        var current = await service.GetAsync(application.Id, TestContext.Current.CancellationToken);
        await service.DeleteAsync(application.Id, current.Version, TestContext.Current.CancellationToken);

        Assert.Null(await repository.GetByIdAsync(application.Id, TestContext.Current.CancellationToken));
        Assert.Contains(application.Id, await repository.FindDocumentsDueForCleanupAsync(DateTime.UtcNow.AddDays(-1), 100, TestContext.Current.CancellationToken));
        var audit = await repository.AuditRepository.GetPageAsync(application.Id, 1, 25, TestContext.Current.CancellationToken);
        Assert.Contains(audit.Events, entry => entry.Action == "Draft deleted" && entry.ActorId == "customer-1");
    }

    [Fact]
    public async Task AnonymousSessionsCannotReadOrChangeEachOthersDrafts()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var owner = CreateService(repository);
        var other = CreateService(repository, new OtherApplicationActor());
        var application = await owner.CreateAsync(TestContext.Current.CancellationToken);
        await owner.SaveQualificationAsync(application.Id, 300_000m, 30_000m, application.Version, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.GetAsync(application.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.SaveQualificationAsync(application.Id, 400_000m, 40_000m, application.Version, TestContext.Current.CancellationToken));
        Assert.Equal(300_000m, (await owner.GetAsync(application.Id, TestContext.Current.CancellationToken)).PropertyValue);
    }

    [Fact]
    public async Task UnauthorizedReadsDoNotExtendDraftRetention()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var owner = CreateService(repository);
        var application = await owner.CreateAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        var cutoff = DateTime.UtcNow;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(repository, new OtherApplicationActor()).GetAsync(application.Id, TestContext.Current.CancellationToken));
        Assert.Contains(await repository.FindExpiredDraftsAsync(cutoff, 100, TestContext.Current.CancellationToken), draft => draft.Id == application.Id);

        await owner.GetAsync(application.Id, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(await repository.FindExpiredDraftsAsync(cutoff, 100, TestContext.Current.CancellationToken), draft => draft.Id == application.Id);
    }

    [Fact]
    public async Task SubmittedApplicationIsNotAvailableToTheCustomerSession()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository);
        var application = await service.CreateAsync(TestContext.Current.CancellationToken);
        await service.SaveQualificationAsync(application.Id, 300_000m, 30_000m, application.Version, TestContext.Current.CancellationToken);
        await service.SaveApplicantAsync(application.Id, "Jane", "Doe", "jane@example.com", "07123456789", (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.SaveFinancesAsync(application.Id, 100_000m, 1_000m, EmploymentType.Employed, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.SaveConsentsAsync(application.Id, false, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.GenerateDecisionInPrincipleAsync(application.Id, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        var products = await service.GetEligibleProductsAsync(application.Id, TestContext.Current.CancellationToken);
        Assert.NotEmpty(products);
        await service.SelectProductAsync(application.Id, products[0].Id, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.SubmitCustomerAsync(application.Id, (await service.GetAsync(application.Id, TestContext.Current.CancellationToken)).Version, Guid.NewGuid(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(application.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteAsync(application.Id, application.Version, TestContext.Current.CancellationToken));
        var staff = CreateService(repository, new StaffApplicationActor());
        var submitted = await staff.GetForStaffAsync(application.Id, TestContext.Current.CancellationToken);
        Assert.Equal(ApplicationStatus.Submitted, submitted.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => staff.DeleteAsync(application.Id, submitted.Version, TestContext.Current.CancellationToken));
        Assert.Equal(ApplicationStatus.Submitted, (await staff.GetForStaffAsync(application.Id, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task SubmissionReceiptExpiresAndIsRestrictedToItsOwner()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(TestContext.Current.CancellationToken);
        await service.SaveQualificationAsync(created.Id, 300_000, 30_000, created.Version, TestContext.Current.CancellationToken);
        await service.SaveApplicantAsync(created.Id, "Jane", "Doe", "jane@example.com", "07123456789", (await service.GetAsync(created.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.SaveFinancesAsync(created.Id, 100_000, 1_000, EmploymentType.Employed, (await service.GetAsync(created.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.SaveConsentsAsync(created.Id, false, (await service.GetAsync(created.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.GenerateDecisionInPrincipleAsync(created.Id, (await service.GetAsync(created.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        await service.SelectProductAsync(created.Id, "fixed-2", (await service.GetAsync(created.Id, TestContext.Current.CancellationToken)).Version, TestContext.Current.CancellationToken);
        var key = Guid.NewGuid();
        var version = (await service.GetAsync(created.Id, TestContext.Current.CancellationToken)).Version;
        await service.SubmitCustomerAsync(created.Id, version, key, TestContext.Current.CancellationToken);

        Assert.NotNull(await service.GetSubmissionReceiptAsync(created.Id, key, TestContext.Current.CancellationToken));
        Assert.Null(await service.GetSubmissionReceiptAsync(created.Id, Guid.NewGuid(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(repository, new OtherApplicationActor()).GetSubmissionReceiptAsync(created.Id, key, TestContext.Current.CancellationToken));

        var submitted = (await repository.GetByIdAsync(created.Id, TestContext.Current.CancellationToken))!;
        var expired = MortgageApplicationSnapshot.FromDomain(submitted) with { UpdatedAtUtc = DateTime.UtcNow.AddHours(-25) };
        var expiredRepository = new InMemoryMortgageApplicationRepository();
        await expiredRepository.AddAsync(expired.ToDomain(), TestContext.Current.CancellationToken);
        Assert.Null(await CreateService(expiredRepository).GetSubmissionReceiptAsync(created.Id, key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingSessionIdentityCannotCreateOrAccessAnApplication()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var application = await CreateService(repository).CreateAsync(TestContext.Current.CancellationToken);
        var service = CreateService(repository, new MissingApplicationActor());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(application.Id, TestContext.Current.CancellationToken));
    }

    private static MortgageApplicationService CreateService(
        InMemoryMortgageApplicationRepository repository,
        IApplicationActor? actor = null,
        IMortgageDocumentStorage? storage = null,
        IProductCatalogClient? catalog = null,
        IEligibilityClient? eligibility = null) =>
        new(
            repository,
            repository.AuditRepository,
            catalog ?? new MockProductCatalogClient(new LocalProductCatalogFallback()),
            eligibility ?? new TestEligibilityClient(),
            storage ?? new InMemoryMortgageDocumentStorage(),
            actor ?? new TestApplicationActor(),
            Options.Create(new MortgageWorkflowOptions { ConsentTermsVersion = "terms-2026-01" }),
            new DocumentUploadValidator(),
            repository,
            NullLogger<MortgageApplicationService>.Instance);

    private sealed class UnavailableEligibilityClient : IEligibilityClient
    {
        public Task<DecisionInPrincipleResult> AssessAsync(decimal propertyValue, decimal deposit, decimal annualIncome,
            bool creditCheckConsent, CancellationToken cancellationToken) => throw new EligibilityUnavailableException();
    }

    private sealed class MutableCatalog : IProductCatalogClient
    {
        public MortgageProductDto Product { get; set; } = new("fixed-2", "Fixed", 4m, 999m, 5m, 2, 90, "original");
        public Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MortgageProductDto>>([Product]);
        public Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default) =>
            GetProductsAsync(cancellationToken);
        public Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MortgageProductDto?>(id == Product.Id ? Product : null);
        public Task<bool> IsProductEligibleAsync(string id, decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == Product.Id);
    }

    private sealed class CommitResponseLostRepository(InMemoryMortgageApplicationRepository inner) : IMortgageApplicationRepository
    {
        public Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);
        public Task<MortgageApplicationSearchResult> SearchAsync(string? search, ApplicationStatus? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
            inner.SearchAsync(search, status, page, pageSize, cancellationToken);
        public Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default) => inner.AddAsync(application, cancellationToken);
        public Task AddAsync(MortgageApplication application, MortgageApplicationAuditEntry auditEntry, CancellationToken cancellationToken = default) =>
            inner.AddAsync(application, auditEntry, cancellationToken);
        public Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default) => inner.UpdateAsync(application, cancellationToken);
        public async Task UpdateAsync(MortgageApplication application, MortgageApplicationAuditEntry auditEntry, CancellationToken cancellationToken = default)
        {
            await inner.UpdateAsync(application, auditEntry, cancellationToken);
            throw new IOException("Commit response lost.");
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
    }

    private sealed class UploadResponseLostStorage : IMortgageDocumentStorage
    {
        private readonly InMemoryMortgageDocumentStorage inner = new();
        public Guid DocumentId { get; private set; }

        public async Task SaveAsync(Guid applicationId, Guid documentId, byte[] contents, CancellationToken cancellationToken = default)
        {
            DocumentId = documentId;
            await inner.SaveAsync(applicationId, documentId, contents, cancellationToken);
            throw new IOException("Storage response lost.");
        }
        public Task<byte[]?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(applicationId, documentId, cancellationToken);
        public Task DeleteAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(applicationId, documentId, cancellationToken);
        public Task DeleteAllAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
            inner.DeleteAllAsync(applicationId, cancellationToken);
    }

    private sealed class PausingDocumentStorage : IMortgageDocumentStorage
    {
        private readonly InMemoryMortgageDocumentStorage inner = new();
        public TaskCompletionSource Stored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid DocumentId { get; private set; }

        public async Task SaveAsync(Guid applicationId, Guid documentId, byte[] contents, CancellationToken cancellationToken = default)
        {
            DocumentId = documentId;
            await inner.SaveAsync(applicationId, documentId, contents, cancellationToken);
            Stored.SetResult();
            await Continue.Task.WaitAsync(cancellationToken);
        }

        public Task<byte[]?> GetAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(applicationId, documentId, cancellationToken);
        public Task DeleteAsync(Guid applicationId, Guid documentId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(applicationId, documentId, cancellationToken);
        public Task DeleteAllAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
            inner.DeleteAllAsync(applicationId, cancellationToken);
    }

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
