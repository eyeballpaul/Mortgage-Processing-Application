using PD.Mortgages.Contracts;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Application.Models;

public static class MortgageContractMapper
{
    public static ApplicationStatusContract ToContract(ApplicationStatus value) => value switch
    {
        ApplicationStatus.InProgress => ApplicationStatusContract.InProgress,
        ApplicationStatus.Submitted => ApplicationStatusContract.Submitted,
        ApplicationStatus.AwaitingInformation => ApplicationStatusContract.AwaitingInformation,
        ApplicationStatus.ApprovedInPrinciple => ApplicationStatusContract.ApprovedInPrinciple,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static ApplicationStatus ToApplication(ApplicationStatusContract value) => value switch
    {
        ApplicationStatusContract.InProgress => ApplicationStatus.InProgress,
        ApplicationStatusContract.Submitted => ApplicationStatus.Submitted,
        ApplicationStatusContract.AwaitingInformation => ApplicationStatus.AwaitingInformation,
        ApplicationStatusContract.ApprovedInPrinciple => ApplicationStatus.ApprovedInPrinciple,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static EmploymentTypeContract ToContract(EmploymentType value) => value switch
    {
        EmploymentType.Employed => EmploymentTypeContract.Employed,
        EmploymentType.SelfEmployed => EmploymentTypeContract.SelfEmployed,
        EmploymentType.Retired => EmploymentTypeContract.Retired,
        EmploymentType.Other => EmploymentTypeContract.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static EmploymentType ToApplication(EmploymentTypeContract value) => value switch
    {
        EmploymentTypeContract.Employed => EmploymentType.Employed,
        EmploymentTypeContract.SelfEmployed => EmploymentType.SelfEmployed,
        EmploymentTypeContract.Retired => EmploymentType.Retired,
        EmploymentTypeContract.Other => EmploymentType.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static DecisionOutcomeContract ToContract(DecisionInPrincipleOutcome value) => value switch
    {
        DecisionInPrincipleOutcome.CreditCheckNotSelected => DecisionOutcomeContract.CreditCheckNotSelected,
        DecisionInPrincipleOutcome.WithinIndicativeRange => DecisionOutcomeContract.WithinIndicativeRange,
        DecisionInPrincipleOutcome.AboveIndicativeRange => DecisionOutcomeContract.AboveIndicativeRange,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static DecisionInPrincipleOutcome ToApplication(DecisionOutcomeContract value) => value switch
    {
        DecisionOutcomeContract.CreditCheckNotSelected => DecisionInPrincipleOutcome.CreditCheckNotSelected,
        DecisionOutcomeContract.WithinIndicativeRange => DecisionInPrincipleOutcome.WithinIndicativeRange,
        DecisionOutcomeContract.AboveIndicativeRange => DecisionInPrincipleOutcome.AboveIndicativeRange,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static ProductResponse ToContract(MortgageProductDto value) => new(value.Id, value.Name, value.InterestRate,
        value.Fee, value.Aprc, value.FixedTermYears, value.MaximumLoanToValue, value.CatalogVersion);
    public static MortgageProductDto ToApplication(ProductResponse value) => new(value.Id, value.Name, value.InterestRate,
        value.Fee, value.Aprc, value.FixedTermYears, value.MaximumLoanToValue, value.CatalogVersion);
    public static ReceiptResponse ToContract(SubmissionReceiptDto value) => new(value.Reference, value.SubmittedAt);
    public static SubmissionReceiptDto ToApplication(ReceiptResponse value) => new(value.Reference, value.SubmittedAt);

    public static ApplicationResponse ToContract(MortgageApplicationDto value) => new(
        value.Id, value.CreatedAtUtc, value.UpdatedAtUtc, ToContract(value.Status), value.FirstName, value.LastName,
        value.Email, value.PhoneNumber, value.PropertyValue, value.Deposit, value.AnnualIncome, value.MonthlyCommitments,
        value.EmploymentType is { } employment ? ToContract(employment) : null, value.CreditCheckConsent,
        value.OpenBankingConsent, value.ConsentsRecorded,
        value.DecisionInPrinciple is { } decision ? new DecisionResponse(ToContract(decision.Outcome),
            decision.RequestedLoanAmount, decision.IndicativeMaximumBorrowing, decision.AssessedAtUtc) : null,
        value.SelectedProductId, value.Documents.Select(document => new DocumentResponse(document.Id, document.FileName,
            document.SizeInBytes, document.UploadedAtUtc)).ToList(), value.CreatedByEmployeeId, value.Version,
        value.SelectedProduct is { } product ? ToContract(product) : null);

    public static MortgageApplicationDto ToApplication(ApplicationResponse value) => new(
        value.Id, value.CreatedAtUtc, value.UpdatedAtUtc, ToApplication(value.Status), value.FirstName, value.LastName,
        value.Email, value.PhoneNumber, value.PropertyValue, value.Deposit, value.AnnualIncome, value.MonthlyCommitments,
        value.EmploymentType is { } employment ? ToApplication(employment) : null, value.CreditCheckConsent,
        value.OpenBankingConsent, value.ConsentsRecorded,
        value.DecisionInPrinciple is { } decision ? new DecisionInPrincipleDto(ToApplication(decision.Outcome),
            decision.RequestedLoanAmount, decision.IndicativeMaximumBorrowing, decision.AssessedAtUtc) : null,
        value.SelectedProductId, value.Documents.Select(document => new MortgageDocumentDto(document.Id, document.FileName,
            document.SizeInBytes, document.UploadedAtUtc)).ToList(), value.CreatedByEmployeeId, value.Version,
        value.SelectedProduct is { } product ? ToApplication(product) : null);

    public static ApplicationPageResponse ToContract(MortgageApplicationSearchPageDto value) => new(
        value.Applications.Select(ToContract).ToList(), value.TotalCount, value.Page, value.PageSize);
    public static MortgageApplicationSearchPageDto ToApplication(ApplicationPageResponse value) => new(
        value.Applications.Select(ToApplication).ToList(), value.TotalCount, value.Page, value.PageSize);
    public static AuditPageResponse ToContract(MortgageApplicationAuditPageDto value) => new(
        value.Events.Select(entry => new AuditEntryResponse(entry.RecordedAtUtc, entry.ActorId, entry.Action)).ToList(),
        value.TotalCount, value.Page, value.PageSize, value.TotalPages);
    public static MortgageApplicationAuditPageDto ToApplication(AuditPageResponse value) => new(
        value.Events.Select(entry => new MortgageApplicationAuditEntryDto(entry.RecordedAtUtc, entry.ActorId, entry.Action)).ToList(),
        value.TotalCount, value.Page, value.PageSize, value.TotalPages);
}
