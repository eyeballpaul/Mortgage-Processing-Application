using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Application.Services;

public interface IMortgageApplicationService
{
    Task<MortgageApplicationDto> CreateAsync(CancellationToken cancellationToken = default);
    Task<MortgageApplicationDto> CreateForStaffAsync(CancellationToken cancellationToken = default);
    Task<MortgageApplicationDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MortgageApplicationDto> GetForStaffAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MortgageApplicationAuditPageDto> GetAuditTrailForStaffAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<MortgageApplicationSearchPageDto> SearchForStaffAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default);
    Task SaveApplicantAsync(Guid id, string firstName, string lastName, string email, string phoneNumber, long expectedVersion, CancellationToken cancellationToken = default);
    Task SaveQualificationAsync(Guid id, decimal propertyValue, decimal deposit, long expectedVersion, CancellationToken cancellationToken = default);
    Task SaveFinancesAsync(Guid id, decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType, long expectedVersion, CancellationToken cancellationToken = default);
    Task SaveConsentsAsync(Guid id, bool creditCheckConsent, long expectedVersion, CancellationToken cancellationToken = default);
    Task<MortgageApplicationDto> GenerateDecisionInPrincipleAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default);
    Task SelectProductAsync(Guid id, string productId, long expectedVersion, CancellationToken cancellationToken = default);
    Task<Guid> AddDocumentAsync(Guid id, string fileName, byte[] contents, long expectedVersion, CancellationToken cancellationToken = default);
    Task<byte[]?> GetDocumentAsync(Guid id, Guid documentId, CancellationToken cancellationToken = default);
    Task SubmitAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default);
    Task<SubmissionReceiptDto> SubmitCustomerAsync(Guid id, long expectedVersion, Guid submissionKey, CancellationToken cancellationToken = default);
    Task<SubmissionReceiptDto?> GetSubmissionReceiptAsync(Guid id, Guid submissionKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default);
}
