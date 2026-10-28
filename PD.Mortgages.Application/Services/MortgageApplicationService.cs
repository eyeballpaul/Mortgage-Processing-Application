using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Domain.Services;

namespace PD.Mortgages.Application.Services;

public sealed class MortgageApplicationService(
    IMortgageApplicationRepository applicationRepository,
    IMortgageProductRepository productRepository,
    IMortgageDocumentStorage documentStorage,
    MortgageEligibilityService eligibilityService) : IMortgageApplicationService
{
    public async Task<MortgageApplicationDto> CreateAsync(CancellationToken cancellationToken = default)
    {
        var application = new MortgageApplication();
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
        var application = await FindAsync(id, cancellationToken);
        application.GenerateIndicativeDecisionInPrinciple();
        await applicationRepository.UpdateAsync(application, cancellationToken);
        return Map(application);
    }

    public async Task SelectProductAsync(Guid id, string productId, CancellationToken cancellationToken = default)
    {
        if (await productRepository.GetByIdAsync(productId, cancellationToken) is null)
        {
            throw new ArgumentException("The selected mortgage product is not available.", nameof(productId));
        }

        await ChangeAsync(id, application => application.SelectProduct(productId), cancellationToken);
    }

    public async Task AddDocumentAsync(Guid id, string fileName, byte[] contents, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        var documentId = Guid.NewGuid();
        await documentStorage.SaveAsync(id, documentId, contents, cancellationToken);
        application.AddDocument(documentId, fileName, contents.LongLength);
        await applicationRepository.UpdateAsync(application, cancellationToken);
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
        (await productRepository.GetAllAsync(cancellationToken)).Select(Map).ToList();

    public async Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await FindAsync(id, cancellationToken);
        if (application.PropertyValue is null || application.Deposit is null)
        {
            return [];
        }

        var products = await productRepository.GetAllAsync(cancellationToken);
        return eligibilityService
            .GetEligibleProducts(application.PropertyValue.Value, application.Deposit.Value, products)
            .Select(Map)
            .ToList();
    }

    public async Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        return product is null ? null : Map(product);
    }

    private async Task ChangeAsync(Guid id, Action<MortgageApplication> change, CancellationToken cancellationToken)
    {
        var application = await FindAsync(id, cancellationToken);
        change(application);
        await applicationRepository.UpdateAsync(application, cancellationToken);
    }

    private async Task<MortgageApplication> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await applicationRepository.GetByIdAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");

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
        application.DecisionInPrinciple,
        application.SelectedProductId,
        application.Documents.ToList());

    private static MortgageProductDto Map(MortgageProduct product) => new(
        product.Id,
        product.Name,
        product.InterestRate,
        product.Fee,
        product.Aprc,
        product.FixedTermYears,
        product.MaximumLoanToValue);
}
