namespace PD.Mortgages.Contracts;

public enum ApplicationStatusContract { InProgress, Submitted, AwaitingInformation, ApprovedInPrinciple }
public enum DecisionOutcomeContract { CreditCheckNotSelected, WithinIndicativeRange, AboveIndicativeRange }

public sealed record DecisionResponse(DecisionOutcomeContract Outcome, decimal RequestedLoanAmount,
    decimal? IndicativeMaximumBorrowing, DateTime AssessedAtUtc);
public sealed record DocumentResponse(Guid Id, string FileName, long SizeInBytes, DateTime UploadedAtUtc);
public sealed record ProductResponse(string Id, string Name, decimal InterestRate, decimal Fee, decimal Aprc,
    int FixedTermYears, int MaximumLoanToValue, string CatalogVersion);
public sealed record ReceiptResponse(Guid Reference, DateTimeOffset SubmittedAt);
public sealed record AuditEntryResponse(DateTime RecordedAtUtc, string ActorId, string Action);
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Events, int TotalCount, int Page, int PageSize, int TotalPages);
public sealed record ApplicationPageResponse(IReadOnlyList<ApplicationResponse> Applications, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
public sealed record ApplicationResponse(
    Guid Id, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, ApplicationStatusContract Status,
    string? FirstName, string? LastName, string? Email, string? PhoneNumber,
    decimal? PropertyValue, decimal? Deposit, decimal? AnnualIncome, decimal? MonthlyCommitments,
    EmploymentTypeContract? EmploymentType, bool CreditCheckConsent, bool OpenBankingConsent,
    bool ConsentsRecorded, DecisionResponse? DecisionInPrinciple, string? SelectedProductId,
    IReadOnlyList<DocumentResponse> Documents, string? CreatedByEmployeeId, long Version, ProductResponse? SelectedProduct);
