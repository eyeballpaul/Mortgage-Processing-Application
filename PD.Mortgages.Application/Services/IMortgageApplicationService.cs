using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Application.Services;

public interface IMortgageApplicationService
{
    Task<MortgageApplicationDto> CreateAsync(CancellationToken cancellationToken = default);
    Task<MortgageApplicationDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveApplicantAsync(Guid id, string firstName, string lastName, string email, string phoneNumber, CancellationToken cancellationToken = default);
    Task SaveQualificationAsync(Guid id, decimal propertyValue, decimal deposit, CancellationToken cancellationToken = default);
    Task SaveFinancesAsync(Guid id, decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType, CancellationToken cancellationToken = default);
    Task SaveConsentsAsync(Guid id, bool creditCheckConsent, bool openBankingConsent, CancellationToken cancellationToken = default);
    Task<MortgageApplicationDto> GenerateDecisionInPrincipleAsync(Guid id, CancellationToken cancellationToken = default);
    Task SelectProductAsync(Guid id, string productId, CancellationToken cancellationToken = default);
    Task AddDocumentAsync(Guid id, string fileName, byte[] contents, CancellationToken cancellationToken = default);
    Task<byte[]?> GetDocumentAsync(Guid id, Guid documentId, CancellationToken cancellationToken = default);
    Task SubmitAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default);
}
