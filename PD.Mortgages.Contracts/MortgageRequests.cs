namespace PD.Mortgages.Contracts;

/// <summary>Employment status used by the public mortgage API.</summary>
public enum EmploymentTypeContract
{
    Employed,
    SelfEmployed,
    Retired,
    Other
}

/// <summary>Applicant details for a draft application.</summary>
public sealed record SaveApplicantRequest(string FirstName, string LastName, string Email, string PhoneNumber);
/// <summary>Property value and available deposit.</summary>
public sealed record SaveQualificationRequest(decimal PropertyValue, decimal Deposit);
/// <summary>Income, commitments and employment type.</summary>
public sealed record SaveFinancesRequest(decimal AnnualIncome, decimal MonthlyCommitments, EmploymentTypeContract EmploymentType);
/// <summary>Customer consent decisions.</summary>
public sealed record SaveConsentsRequest(bool CreditCheckConsent);
/// <summary>The selected mortgage product identifier.</summary>
public sealed record SelectProductRequest(string ProductId);
/// <summary>A document to attach to an application.</summary>
public sealed record UploadDocumentRequest(string FileName, byte[] Contents);
/// <summary>Identifies a document added to an application.</summary>
public sealed record UploadDocumentResponse(Guid Id);
/// <summary>Identifies a customer submission attempt.</summary>
public sealed record SubmitCustomerRequest(Guid SubmissionKey);
