using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace PD.Mortgages.Application.Services;

public sealed class MortgageApplicationService(
    IMortgageApplicationRepository applicationRepository,
    IMortgageApplicationAuditRepository auditRepository,
    IProductCatalogClient productCatalogClient,
    IEligibilityClient eligibilityClient,
    IMortgageDocumentStorage documentStorage,
    IApplicationActor applicationActor,
    IOptions<MortgageWorkflowOptions> workflowOptions,
    IDocumentUploadValidator documentValidator,
    IMortgageDraftLifecycle draftLifecycle,
    ILogger<MortgageApplicationService> logger) : IMortgageApplicationService
{
    public async Task<MortgageApplicationDto> CreateAsync(CancellationToken cancellationToken = default)
    {
        var application = new MortgageApplication();
        AssignOwner(application);
        application.RecordAudit(GetActorId(), "Application created");
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
        await TouchDraftAsync(application, cancellationToken);
        return Map(application);
    }

    public async Task<MortgageApplicationAuditPageDto> GetAuditTrailForStaffAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        EnsureStaff();
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken);
        if (application is not null)
        {
            await TouchDraftAsync(application, cancellationToken);
            await MigrateLegacyAuditAsync(application, cancellationToken);
        }
        var result = await auditRepository.GetPageAsync(id, page, pageSize, cancellationToken);
        if (application is null && result.TotalCount == 0)
            throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");
        return new MortgageApplicationAuditPageDto(
            result.Events.Select(entry => new MortgageApplicationAuditEntryDto(entry.RecordedAtUtc, entry.ActorId, entry.Action)).ToList(),
            result.TotalCount, result.Page, result.PageSize, result.TotalPages);
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

    public async Task DeleteAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var application = await FindOwnedAsync(id, cancellationToken);
        CheckVersion(application, expectedVersion);
        if (application.Status != ApplicationStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress applications can be deleted.");
        }

        await MigrateLegacyAuditAsync(application, cancellationToken);
        var auditEntry = new MortgageApplicationAuditEntry(DateTime.UtcNow, GetActorId(), "Draft deleted");
        if (!await draftLifecycle.TryDeleteDraftAsync(id, expectedVersion, auditEntry, null, cancellationToken))
        {
            throw new MortgageConcurrencyException();
        }
    }

    public Task SaveApplicantAsync(Guid id, string firstName, string lastName, string email, string phoneNumber, long expectedVersion, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, expectedVersion, application => application.UpdateApplicant(firstName, lastName, email, phoneNumber), "Applicant details updated", cancellationToken);

    public Task SaveQualificationAsync(Guid id, decimal propertyValue, decimal deposit, long expectedVersion, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, expectedVersion, application => application.UpdateQualification(propertyValue, deposit), "Qualification updated", cancellationToken);

    public Task SaveFinancesAsync(Guid id, decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType, long expectedVersion, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, expectedVersion, application => application.UpdateFinances(annualIncome, monthlyCommitments, employmentType), "Finances updated", cancellationToken);

    public Task SaveConsentsAsync(Guid id, bool creditCheckConsent, bool openBankingConsent, long expectedVersion, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            id,
            expectedVersion,
            application => application.RecordConsents(
                creditCheckConsent,
                openBankingConsent,
                workflowOptions.Value.ConsentTermsVersion,
                GetActorId()),
            "Customer consents recorded",
            cancellationToken);

    public async Task<MortgageApplicationDto> GenerateDecisionInPrincipleAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        CheckVersion(application, expectedVersion);
        if (application.DecisionInPrinciple is not null)
        {
            return Map(application);
        }

        var inputs = application.GetDecisionInputs();
        var decision = await eligibilityClient.AssessAsync(inputs.PropertyValue, inputs.Deposit, inputs.AnnualIncome,
            inputs.CreditCheckConsent, cancellationToken);
        application = await FindAsync(id, cancellationToken);
        CheckVersion(application, expectedVersion);
        application.RecordIndicativeDecision(decision);
        await MigrateLegacyAuditAsync(application, cancellationToken);
        application.RecordAudit(GetActorId(), "Decision in Principle generated");
        await PersistMutationAsync(application, cancellationToken);
        return Map(application);
    }

    public async Task SelectProductAsync(Guid id, string productId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        CheckVersion(application, expectedVersion);
        if (application.PropertyValue is null || application.Deposit is null)
        {
            throw new InvalidOperationException("Complete the property and deposit details before selecting a product.");
        }

        var eligibleProducts = await productCatalogClient.GetEligibleProductsAsync(
            application.PropertyValue.Value, application.Deposit.Value, cancellationToken);
        var product = eligibleProducts.SingleOrDefault(item => item.Id == productId)
            ?? throw new ArgumentException("The selected mortgage product is not available or eligible for this application.", nameof(productId));

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
        application.RecordAudit(GetActorId(), $"Product '{productId}' selected");
        await PersistMutationAsync(application, cancellationToken);
    }

    public async Task<Guid> AddDocumentAsync(Guid id, string fileName, byte[] contents, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var documentId = Guid.Empty;
        var documentStored = false;
        var persistenceAttempted = false;
        try
        {
            var application = await FindAsync(id, cancellationToken);
            CheckVersion(application, expectedVersion);
            await using var stream = new MemoryStream(contents, writable: false);
            var validation = await documentValidator.ValidateAsync(fileName, contents.LongLength, stream,
                application.Documents.Count, application.Documents.Sum(document => document.SizeInBytes), cancellationToken);
            if (!validation.IsValid)
            {
                throw new ArgumentException(validation.ErrorMessage, nameof(fileName));
            }
            await MigrateLegacyAuditAsync(application, cancellationToken);
            documentId = Guid.NewGuid();
            await documentStorage.SaveAsync(id, documentId, validation.Contents!, cancellationToken);
            documentStored = true;
            application.AddDocument(documentId, validation.SafeFileName!, validation.Contents!.LongLength);
            application.RecordAudit(GetActorId(), "Document uploaded");
            persistenceAttempted = true;
            await PersistMutationAsync(application, cancellationToken);
            return documentId;
        }
        catch (Exception operationException)
        {
            if (documentStored && (!persistenceAttempted || operationException is MortgageConcurrencyException))
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
            else if (documentId != Guid.Empty)
            {
                logger.LogWarning(
                    "Document upload outcome is uncertain for application {ApplicationId}, document {DocumentId}; stored content was retained for reconciliation.",
                    id, documentId);
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

    public Task SubmitAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureStaff();
        return ChangeAsync(id, expectedVersion, application => application.Submit(), "Application submitted", cancellationToken);
    }

    public async Task<SubmissionReceiptDto> SubmitCustomerAsync(Guid id, long expectedVersion, Guid submissionKey, CancellationToken cancellationToken = default)
    {
        if (submissionKey == Guid.Empty) throw new ArgumentException("A valid submission key is required.", nameof(submissionKey));
        var application = await FindCustomerSubmissionAsync(id, cancellationToken);
        var receipt = GetReceipt(application, submissionKey);
        if (receipt is not null && application.SubmissionExpectedVersion == expectedVersion) return receipt;
        if (application.Status != ApplicationStatus.InProgress)
            throw new UnauthorizedAccessException("The application is not available in this session.");

        CheckVersion(application, expectedVersion);
        await MigrateLegacyAuditAsync(application, cancellationToken);
        application.Submit(submissionKey);
        application.RecordAudit(GetActorId(), "Application submitted");
        try
        {
            await PersistMutationAsync(application, cancellationToken);
        }
        catch (MortgageConcurrencyException)
        {
            var latest = await FindCustomerSubmissionAsync(id, cancellationToken);
            var concurrentReceipt = GetReceipt(latest, submissionKey);
            if (concurrentReceipt is not null && latest.SubmissionExpectedVersion == expectedVersion) return concurrentReceipt;
            throw;
        }
        return GetReceipt(application, submissionKey)
            ?? throw new InvalidOperationException("The submission receipt could not be created.");
    }

    public async Task<SubmissionReceiptDto?> GetSubmissionReceiptAsync(Guid id, Guid submissionKey, CancellationToken cancellationToken = default)
    {
        if (submissionKey == Guid.Empty) throw new ArgumentException("A valid submission key is required.", nameof(submissionKey));
        return GetReceipt(await FindCustomerSubmissionAsync(id, cancellationToken), submissionKey);
    }

    private async Task<MortgageApplication> FindCustomerSubmissionAsync(Guid id, CancellationToken cancellationToken)
    {
        if (applicationActor.IsStaff || string.IsNullOrWhiteSpace(applicationActor.SubjectId))
            throw new UnauthorizedAccessException("An active customer session is required.");
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");
        if (!string.Equals(application.OwnerId, applicationActor.SubjectId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The application is not available in this session.");
        await TouchDraftAsync(application, cancellationToken);
        return application;
    }

    private static SubmissionReceiptDto? GetReceipt(MortgageApplication application, Guid submissionKey) =>
        application.Status == ApplicationStatus.Submitted &&
        application.SubmissionKey == submissionKey &&
        application.UpdatedAtUtc >= DateTime.UtcNow.AddHours(-24)
            ? new SubmissionReceiptDto(application.Id, new DateTimeOffset(DateTime.SpecifyKind(application.UpdatedAtUtc, DateTimeKind.Utc)))
            : null;

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

    private async Task ChangeAsync(Guid id, long expectedVersion, Action<MortgageApplication> change, string auditAction, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        CheckVersion(application, expectedVersion);
        await MigrateLegacyAuditAsync(application, cancellationToken);
        change(application);
        application.RecordAudit(GetActorId(), auditAction);
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
        try
        {
            await applicationRepository.UpdateAsync(application, auditEntry, cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Message == MortgageConcurrencyException.ConflictMessage)
        {
            throw new MortgageConcurrencyException(exception);
        }
    }

    private static void CheckVersion(MortgageApplication application, long expectedVersion)
    {
        if (application.Version != expectedVersion)
            throw new MortgageConcurrencyException();
    }

    private async Task<MortgageApplication> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await FindOwnedAsync(id, cancellationToken);

    private async Task<MortgageApplication> FindOwnedAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");

        if (applicationActor.IsStaff)
        {
            EnsureStaff();
            await TouchDraftAsync(application, cancellationToken);
            return application;
        }

        if (string.IsNullOrWhiteSpace(applicationActor.SubjectId) ||
            !string.Equals(application.OwnerId, applicationActor.SubjectId, StringComparison.Ordinal) ||
            application.Status != ApplicationStatus.InProgress)
        {
            throw new UnauthorizedAccessException("The application is not available in this session.");
        }

        await TouchDraftAsync(application, cancellationToken);
        return application;
    }

    private async Task TouchDraftAsync(MortgageApplication application, CancellationToken cancellationToken)
    {
        if (application.Status == ApplicationStatus.InProgress &&
            !await draftLifecycle.TouchActivityAsync(application.Id, cancellationToken))
            throw new MortgageConcurrencyException();
    }

    private string GetActorId() =>
        !string.IsNullOrWhiteSpace(applicationActor.SubjectId)
            ? applicationActor.SubjectId
            : throw new UnauthorizedAccessException("An application actor is required.");

    private void EnsureStaff()
    {
        if (!applicationActor.IsAuthenticated || !applicationActor.IsStaff ||
            string.IsNullOrWhiteSpace(applicationActor.SubjectId))
        {
            throw new UnauthorizedAccessException("An authenticated staff member is required.");
        }
    }

    private void AssignOwner(MortgageApplication application)
    {
        if (!applicationActor.IsStaff && !string.IsNullOrWhiteSpace(applicationActor.SubjectId))
        {
            application.AssignOwner(applicationActor.SubjectId);
            return;
        }

        throw new UnauthorizedAccessException("An active customer session is required to create an application.");
    }

    private static MortgageApplicationDto Map(MortgageApplication application) => new(
        application.Id,
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
        application.CreatedByEmployeeId,
        application.Version,
        application.SelectedProduct is null
            ? null
            : new MortgageProductDto(
                application.SelectedProduct.Id,
                application.SelectedProduct.Name,
                application.SelectedProduct.InterestRate,
                application.SelectedProduct.Fee,
                application.SelectedProduct.Aprc,
                application.SelectedProduct.FixedTermYears,
                application.SelectedProduct.MaximumLoanToValue,
                application.SelectedProduct.CatalogVersion));

}
