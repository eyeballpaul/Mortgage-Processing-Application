using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace PD.Mortgages.Application.Services;

public sealed class MortgageApplicationService(
    IMortgageApplicationRepository applicationRepository,
    IMortgageApplicationAuditRepository auditRepository,
    IProductCatalogClient productCatalogClient,
    IMortgageDocumentStorage documentStorage,
    IApplicationActor applicationActor,
    IOptions<ApplicationSecurityOptions> securityOptions,
    IOptions<MortgageWorkflowOptions> workflowOptions) : IMortgageApplicationService
{
    public async Task<MortgageApplicationDto> CreateAsync(CancellationToken cancellationToken = default)
    {
        var application = new MortgageApplication();
        AssignOwner(application);
        application.RecordAudit(GetActorId(application), "Application created");
        var auditEntry = application.AuditTrail.Last();
        application.ClearAuditTrail();
        await applicationRepository.AddAsync(application, auditEntry, cancellationToken);
        return Map(application);
    }

    public async Task<MortgageApplicationDto> CreateForStaffAsync(CancellationToken cancellationToken = default)
    {
        EnsureStaff();
        var employeeId = applicationActor.SubjectId;
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            throw new UnauthorizedAccessException("A stable employee identity is required to create an application.");
        }

        var application = MortgageApplication.CreateForEmployee(employeeId);
        application.RecordAudit(employeeId, "Application created by staff");
        var auditEntry = application.AuditTrail.Last();
        application.ClearAuditTrail();
        await applicationRepository.AddAsync(application, auditEntry, cancellationToken);
        return Map(application);
    }

    public async Task<MortgageApplicationDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Map(await FindAsync(id, cancellationToken));

    public async Task<MortgageApplicationDto> GetForStaffAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureStaff();
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");
        return Map(application);
    }

    public async Task<MortgageApplicationAuditPageDto> GetAuditTrailForStaffAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        EnsureStaff();
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");
        await MigrateLegacyAuditAsync(application, cancellationToken);
        var result = await auditRepository.GetPageAsync(id, page, pageSize, cancellationToken);
        return new MortgageApplicationAuditPageDto(result.Events, result.TotalCount, result.Page, result.PageSize, result.TotalPages);
    }

    public async Task<MortgageApplicationSearchPageDto> SearchForStaffAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureStaff();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var results = await applicationRepository.SearchAsync(search?.Trim(), status, page, pageSize, cancellationToken);
        return new MortgageApplicationSearchPageDto(
            results.Applications.Select(Map).ToList(),
            results.TotalCount,
            results.Page,
            results.PageSize);
    }

    public async Task LinkToCustomerAsync(Guid id, string customerSubjectId, CancellationToken cancellationToken = default)
    {
        EnsureStaff();
        await ChangeAsync(
            id,
            application => application.LinkToCustomer(customerSubjectId),
            "Application linked to customer",
            cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await FindOwnedAsync(id, cancellationToken);
        await documentStorage.DeleteAllAsync(id, cancellationToken);
        await applicationRepository.DeleteAsync(id, cancellationToken);
    }

    public Task SaveApplicantAsync(Guid id, string firstName, string lastName, string email, string phoneNumber, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.UpdateApplicant(firstName, lastName, email, phoneNumber), "Applicant details updated", cancellationToken);

    public Task SaveQualificationAsync(Guid id, decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.UpdateQualification(propertyValue, deposit), "Qualification updated", cancellationToken);

    public Task SaveFinancesAsync(Guid id, decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.UpdateFinances(annualIncome, monthlyCommitments, employmentType), "Finances updated", cancellationToken);

    public Task SaveConsentsAsync(Guid id, bool creditCheckConsent, bool openBankingConsent, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            id,
            application => application.RecordConsents(
                creditCheckConsent,
                openBankingConsent,
                workflowOptions.Value.ConsentTermsVersion,
                GetActorId(application)),
            "Customer consents recorded",
            cancellationToken);

    public async Task<MortgageApplicationDto> GenerateDecisionInPrincipleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application.DecisionInPrinciple is not null)
        {
            return Map(application);
        }

        if (application.CreditCheckConsent)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            application = await FindAsync(id, cancellationToken);
            if (application.DecisionInPrinciple is not null)
            {
                return Map(application);
            }
        }

        application.GenerateIndicativeDecisionInPrinciple();
        await MigrateLegacyAuditAsync(application, cancellationToken);
        application.RecordAudit(GetActorId(application), "Decision in Principle generated");
        await PersistMutationAsync(application, cancellationToken);
        return Map(application);
    }

    public async Task SelectProductAsync(Guid id, string productId, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        var product = await productCatalogClient.GetProductAsync(productId, cancellationToken);
        if (product is null)
        {
            throw new ArgumentException("The selected mortgage product is not available.", nameof(productId));
        }

        if (application.PropertyValue is null || application.Deposit is null)
        {
            throw new InvalidOperationException("Complete the property and deposit details before selecting a product.");
        }

        if (!await productCatalogClient.IsProductEligibleAsync(
                productId,
                application.PropertyValue.Value,
                application.Deposit.Value,
                cancellationToken))
        {
            throw new ArgumentException("The selected mortgage product is not eligible for this application.", nameof(productId));
        }

        application.SelectProduct(productId, new SelectedMortgageProductSnapshot(
            product.Id,
            product.Name,
            product.InterestRate,
            product.Fee,
            product.Aprc,
            product.FixedTermYears,
            product.MaximumLoanToValue,
            product.CatalogVersion));
        await MigrateLegacyAuditAsync(application, cancellationToken);
        application.RecordAudit(GetActorId(application), $"Product '{productId}' selected");
        await PersistMutationAsync(application, cancellationToken);
    }

    public async Task AddDocumentAsync(Guid id, string fileName, byte[] contents, CancellationToken cancellationToken = default)
    {
        var documentId = Guid.Empty;
        var documentStored = false;
        try
        {
            var application = await FindAsync(id, cancellationToken);
            await MigrateLegacyAuditAsync(application, cancellationToken);
            documentId = Guid.NewGuid();
            await documentStorage.SaveAsync(id, documentId, contents, cancellationToken);
            documentStored = true;
            application.AddDocument(documentId, fileName, contents.LongLength);
            application.RecordAudit(GetActorId(application), "Document uploaded");
            await PersistMutationAsync(application, cancellationToken);
        }
        catch (Exception operationException)
        {
            if (documentStored)
            {
                try
                {
                    await documentStorage.DeleteAsync(id, documentId, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "The document upload failed and its stored content could not be removed.",
                        operationException,
                        cleanupException);
                }
            }

            throw;
        }
    }

    public async Task<byte[]?> GetDocumentAsync(Guid id, Guid documentId, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        return application.Documents.Any(document => document.Id == documentId)
            ? await documentStorage.GetAsync(id, documentId, cancellationToken)
            : null;
    }

    public Task SubmitAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.Submit(), "Application submitted", cancellationToken);

    public async Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        await productCatalogClient.GetProductsAsync(cancellationToken);

    public async Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application.PropertyValue is null || application.Deposit is null)
        {
            return [];
        }

        return await productCatalogClient.GetEligibleProductsAsync(
            application.PropertyValue.Value,
            application.Deposit.Value,
            cancellationToken);
    }

    public async Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        return await productCatalogClient.GetProductAsync(id, cancellationToken);
    }

    private async Task ChangeAsync(Guid id, Action<MortgageApplication> change, string auditAction, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        await MigrateLegacyAuditAsync(application, cancellationToken);
        change(application);
        application.RecordAudit(GetActorId(application), auditAction);
        await PersistMutationAsync(application, cancellationToken);
    }

    private async Task MigrateLegacyAuditAsync(MortgageApplication application, CancellationToken cancellationToken)
    {
        if (application.AuditTrail.Count == 0)
        {
            return;
        }

        await auditRepository.AppendLegacyAsync(application.Id, application.AuditTrail.ToList(), cancellationToken);
        application.ClearAuditTrail();
    }

    private async Task PersistMutationAsync(MortgageApplication application, CancellationToken cancellationToken)
    {
        var auditEntry = application.AuditTrail.Last();
        application.ClearAuditTrail();
        await applicationRepository.UpdateAsync(application, auditEntry, cancellationToken);
    }

    private async Task<MortgageApplication> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await FindOwnedAsync(id, cancellationToken);

    private async Task<MortgageApplication> FindOwnedAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");

        if (securityOptions.Value.RequireAuthenticatedUser && !applicationActor.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("An authenticated user is required.");
        }

        if (applicationActor.IsStaff)
        {
            EnsureStaff();
            return application;
        }

        if (securityOptions.Value.RequireAuthenticatedUser &&
            !string.Equals(application.OwnerId, applicationActor.SubjectId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The application does not belong to the current user.");
        }

        return application;
    }

    private string GetActorId(MortgageApplication application) =>
        !string.IsNullOrWhiteSpace(applicationActor.SubjectId)
            ? applicationActor.SubjectId
            : application.OwnerId ?? "development-anonymous";

    private void EnsureStaff()
    {
        if (!applicationActor.IsAuthenticated || !applicationActor.IsStaff)
        {
            throw new UnauthorizedAccessException("An authenticated staff member is required.");
        }
    }

    private void AssignOwner(MortgageApplication application)
    {
        if (applicationActor.IsAuthenticated && !string.IsNullOrWhiteSpace(applicationActor.SubjectId))
        {
            application.AssignOwner(applicationActor.SubjectId);
            return;
        }

        if (securityOptions.Value.RequireAuthenticatedUser)
        {
            throw new UnauthorizedAccessException("An authenticated user is required to create an application.");
        }

        application.AssignOwner("development-anonymous");
    }

    private static MortgageApplicationDto Map(MortgageApplication application) => new(
        application.Id,
        application.OwnerId,
        application.CreatedAtUtc,
        application.UpdatedAtUtc,
        application.Status,
        application.FirstName,
        application.LastName,
        application.Email,
        application.PhoneNumber,
        application.PropertyValue,
        application.Deposit,
        application.AnnualIncome,
        application.MonthlyCommitments,
        application.EmploymentType,
        application.CreditCheckConsent,
        application.OpenBankingConsent,
        application.ConsentsRecorded,
        application.DecisionInPrinciple is null
            ? null
            : new DecisionInPrincipleDto(
                application.DecisionInPrinciple.Outcome,
                application.DecisionInPrinciple.RequestedLoanAmount,
                application.DecisionInPrinciple.IndicativeMaximumBorrowing,
                application.DecisionInPrinciple.AssessedAtUtc),
        application.SelectedProductId,
        application.Documents
            .Select(document => new MortgageDocumentDto(
                document.Id,
                document.FileName,
                document.SizeInBytes,
                document.UploadedAtUtc))
            .ToList(),
        application.CreatedByEmployeeId);

}
