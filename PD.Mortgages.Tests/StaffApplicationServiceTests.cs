using Microsoft.Extensions.Options;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Infrastructure.Persistence;
using PD.Mortgages.Infrastructure.Products;
using PD.Mortgages.Infrastructure.Repositories;
using PD.Mortgages.Infrastructure.Storage;

namespace PD.Mortgages.Tests;

public sealed class StaffApplicationServiceTests
{
    [Fact]
    public async Task StaffCanSearchApplicationsAndRecordsAuditEvents()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository, new TestApplicationActor("staff-42", true, true));
        var created = await service.CreateForStaffAsync();
        await service.SaveApplicantAsync(created.Id, "Jane", "Doe", "jane@example.com", "07123456789");
        await service.SaveQualificationAsync(created.Id, 300_000m, 30_000m);

        var matching = await service.SearchForStaffAsync("jane@example.com", ApplicationStatus.InProgress, 1, 25);
        var result = Assert.Single(matching.Applications);
        var details = await service.GetForStaffAsync(created.Id);
        var audit = await service.GetAuditTrailForStaffAsync(created.Id, 1, 25);

        Assert.Equal(1, matching.TotalCount);
        Assert.Equal(created.Id, result.Id);
        Assert.Null(details.OwnerId);
        Assert.Equal("staff-42", details.CreatedByEmployeeId);
        Assert.Equal("Qualification updated", audit.Events[0].Action);
        Assert.Equal("Applicant details updated", audit.Events[1].Action);
        Assert.Equal("Application created by staff", audit.Events[2].Action);
        Assert.All(audit.Events, entry => Assert.Equal("staff-42", entry.ActorId));
    }

    [Fact]
    public async Task AuditHistoryReturnsOnlyRequestedPageInNewestFirstOrder()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository, new TestApplicationActor("staff-42", true, true));
        var application = await service.CreateForStaffAsync();
        for (var index = 0; index < 5; index++)
        {
            await service.SaveApplicantAsync(application.Id, "Jane", "Doe", $"jane{index}@example.com", "07123456789");
        }

        var firstPage = await service.GetAuditTrailForStaffAsync(application.Id, 1, 2);
        var secondPage = await service.GetAuditTrailForStaffAsync(application.Id, 2, 2);

        Assert.Equal(6, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Events.Count);
        Assert.Equal(2, secondPage.Events.Count);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, secondPage.Page);
        Assert.All(firstPage.Events, entry => Assert.Equal("Applicant details updated", entry.Action));
        Assert.DoesNotContain(secondPage.Events, firstPage.Events.Contains);
    }

    [Fact]
    public async Task StaffAuditHistoryMigratesLegacyEventsFromApplicationRecord()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var application = MortgageApplication.CreateForEmployee("staff-42");
        application.RecordAudit("staff-42", "Legacy event");
        await repository.AddAsync(application);
        var service = CreateService(repository, new TestApplicationActor("staff-42", true, true));

        var audit = await service.GetAuditTrailForStaffAsync(application.Id, 1, 25);

        Assert.Equal("Legacy event", Assert.Single(audit.Events).Action);
        Assert.Empty(application.AuditTrail);
    }

    [Fact]
    public async Task NonStaffCannotUseStaffQueries()
    {
        var service = CreateService(new InMemoryMortgageApplicationRepository(), new TestApplicationActor("customer-1", true, false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchForStaffAsync(null, null, 1, 25));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuditTrailForStaffAsync(Guid.NewGuid(), 1, 25));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateForStaffAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LinkToCustomerAsync(Guid.NewGuid(), "customer-1"));
    }

    [Fact]
    public async Task StaffCreationRequiresAnEmployeeSubjectId()
    {
        var service = CreateService(
            new InMemoryMortgageApplicationRepository(),
            new TestApplicationActor(null, true, true));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateForStaffAsync());
    }

    [Fact]
    public async Task CustomerCreatedApplicationHasNoEmployeeCreator()
    {
        var service = CreateService(
            new InMemoryMortgageApplicationRepository(),
            new TestApplicationActor("customer-1", true, false));

        var application = await service.CreateAsync();

        Assert.Equal("customer-1", application.OwnerId);
        Assert.Null(application.CreatedByEmployeeId);
    }

    [Fact]
    public async Task CustomerCannotReadApplicationOwnedByAnotherActor()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var staffService = CreateService(repository, new TestApplicationActor("staff-42", true, true));
        var application = await staffService.CreateForStaffAsync();
        var customerService = CreateService(repository, new TestApplicationActor("customer-1", true, false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => customerService.GetAsync(application.Id));
    }

    [Fact]
    public async Task StaffSearchReturnsOnlyRequestedPageAndCorrectFilteredCount()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var service = CreateService(repository, new TestApplicationActor("staff-42", true, true));
        foreach (var email in new[] { "one@example.com", "two@example.com", "three@example.com" })
        {
            var application = await service.CreateForStaffAsync();
            await service.SaveApplicantAsync(application.Id, "Test", "Applicant", email, "07123456789");
        }

        var firstPage = await service.SearchForStaffAsync(null, ApplicationStatus.InProgress, 1, 2);
        var lastPage = await service.SearchForStaffAsync(null, ApplicationStatus.InProgress, 99, 2);
        var filtered = await service.SearchForStaffAsync("two@example.com", ApplicationStatus.InProgress, 1, 2);
        var byCreator = await service.SearchForStaffAsync("staff-42", ApplicationStatus.InProgress, 1, 25);
        var noMatches = await service.SearchForStaffAsync("missing", ApplicationStatus.InProgress, 1, 25);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Applications.Count);
        Assert.Equal(1, lastPage.Applications.Count);
        Assert.Equal(2, lastPage.Page);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("two@example.com", Assert.Single(filtered.Applications).Email);
        Assert.Equal(3, byCreator.TotalCount);
        Assert.All(byCreator.Applications, item => Assert.Equal("staff-42", item.CreatedByEmployeeId));
        Assert.Equal(0, noMatches.TotalCount);
        Assert.Empty(noMatches.Applications);
    }

    [Fact]
    public async Task StaffCanLinkUnassignedApplicationToCustomerSubjectWithoutChangingOwnerLater()
    {
        var repository = new InMemoryMortgageApplicationRepository();
        var staffService = CreateService(repository, new TestApplicationActor("staff-42", true, true));
        var application = await staffService.CreateForStaffAsync();

        await staffService.LinkToCustomerAsync(application.Id, "customer-subject-17");

        var details = await staffService.GetForStaffAsync(application.Id);
        var audit = await staffService.GetAuditTrailForStaffAsync(application.Id, 1, 25);
        var customerService = CreateService(repository, new TestApplicationActor("customer-subject-17", true, false));
        var otherCustomerService = CreateService(repository, new TestApplicationActor("customer-subject-18", true, false));

        Assert.Equal("customer-subject-17", details.OwnerId);
        Assert.Equal("Application linked to customer", audit.Events[0].Action);
        Assert.Equal("staff-42", audit.Events[0].ActorId);
        Assert.Equal(application.Id, (await customerService.GetAsync(application.Id)).Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherCustomerService.GetAsync(application.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => staffService.LinkToCustomerAsync(application.Id, "customer-subject-18"));
    }

    private static MortgageApplicationService CreateService(InMemoryMortgageApplicationRepository repository, IApplicationActor actor) =>
        new(
            repository,
            new InMemoryMortgageApplicationAuditRepository(),
            new MockProductCatalogClient(new LocalProductCatalogFallback()),
            new InMemoryMortgageDocumentStorage(),
            actor,
            Options.Create(new ApplicationSecurityOptions { RequireAuthenticatedUser = true }));

    private sealed class TestApplicationActor(string? subjectId, bool isAuthenticated, bool isStaff) : IApplicationActor
    {
        public string? SubjectId { get; } = subjectId;
        public bool IsAuthenticated { get; } = isAuthenticated;
        public bool IsStaff { get; } = isStaff;
    }
}
