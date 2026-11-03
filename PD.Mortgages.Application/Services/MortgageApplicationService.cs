using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Security;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Domain.Entities;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace PD.Mortgages.Application.Services;

public sealed class MortgageApplicationService(
    IMortgageApplicationRepository applicationRepository,
    IProductCatalogClient productCatalogClient,
    IMortgageDocumentStorage documentStorage,
    IApplicationActor applicationActor,
    IOptions<ApplicationSecurityOptions> securityOptions) : IMortgageApplicationService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> MutationGates = new();
    public async Task<MortgageApplicationDto> CreateAsync(CancellationToken cancellationToken = default)
    {
        var application = new MortgageApplication();
        AssignOwner(application);
        await applicationRepository.AddAsync(application, cancellationToken);
        return Map(application);
    }

    public async Task<MortgageApplicationDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Map(await FindAsync(id, cancellationToken));

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await documentStorage.DeleteAllAsync(id, cancellationToken);
        await applicationRepository.DeleteAsync(id, cancellationToken);
    }

    public Task SaveApplicantAsync(Guid id, string firstName, string lastName, string email, string phoneNumber, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.UpdateApplicant(firstName, lastName, email, phoneNumber), cancellationToken);

    public Task SaveQualificationAsync(Guid id, decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.UpdateQualification(propertyValue, deposit), cancellationToken);

    public Task SaveFinancesAsync(Guid id, decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.UpdateFinances(annualIncome, monthlyCommitments, employmentType), cancellationToken);

    public Task SaveConsentsAsync(Guid id, bool creditCheckConsent, bool openBankingConsent, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, application => application.RecordConsents(creditCheckConsent, openBankingConsent), cancellationToken);

    public async Task<MortgageApplicationDto> GenerateDecisionInPrincipleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var gate = MutationGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
        var application = await FindAsync(id, cancellationToken);
        if (application.DecisionInPrinciple is not null)
        {
            return Map(application);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        application = await FindAsync(id, cancellationToken);
        if (application.DecisionInPrinciple is not null)
        {
            return Map(application);
        }

        application.GenerateIndicativeDecisionInPrinciple();
        await applicationRepository.UpdateAsync(application, cancellationToken);
        return Map(application);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SelectProductAsync(Guid id, string productId, CancellationToken cancellationToken = default)
    {
        var gate = MutationGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
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
        await applicationRepository.UpdateAsync(application, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AddDocumentAsync(Guid id, string fileName, byte[] contents, CancellationToken cancellationToken = default)
    {
        var gate = MutationGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        var documentId = Guid.Empty;
        try
        {
        var application = await FindAsync(id, cancellationToken);
        documentId = Guid.NewGuid();
        await documentStorage.SaveAsync(id, documentId, contents, cancellationToken);
        application.AddDocument(documentId, fileName, contents.LongLength);
        await applicationRepository.UpdateAsync(application, cancellationToken);
        }
        catch
        {
            try
            {
                await documentStorage.DeleteAsync(id, documentId, cancellationToken);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            gate.Release();
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
        ChangeAsync(id, application => application.Submit(), cancellationToken);

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

    private async Task ChangeAsync(Guid id, Action<MortgageApplication> change, CancellationToken cancellationToken)
    {
        var gate = MutationGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
        var application = await FindAsync(id, cancellationToken);
        change(application);
        await applicationRepository.UpdateAsync(application, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
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

        if (securityOptions.Value.RequireAuthenticatedUser &&
            application.OwnerId is not null &&
            !string.Equals(application.OwnerId, applicationActor.SubjectId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The application does not belong to the current user.");
        }

        return application;
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
            .ToList());

}
