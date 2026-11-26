using System.ComponentModel.DataAnnotations;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Domain.Entities;

public sealed class MortgageApplication
{
    private readonly List<MortgageDocument> _documents = [];
    private readonly List<MortgageApplicationAuditEntry> _auditTrail = [];
    private readonly List<CustomerConsent> _consentHistory = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public long Version { get; private set; }
    public string? OwnerId { get; private set; }
    public string? CreatedByEmployeeId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public ApplicationStatus Status { get; private set; } = ApplicationStatus.InProgress;
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public decimal? PropertyValue { get; private set; }
    public decimal? Deposit { get; private set; }
    public decimal? AnnualIncome { get; private set; }
    public decimal? MonthlyCommitments { get; private set; }
    public EmploymentType? EmploymentType { get; private set; }
    public bool CreditCheckConsent { get; private set; }
    public bool OpenBankingConsent { get; private set; }
    public bool ConsentsRecorded { get; private set; }
    public DecisionInPrincipleResult? DecisionInPrinciple { get; private set; }
    public string? SelectedProductId { get; private set; }
    public SelectedMortgageProductSnapshot? SelectedProduct { get; private set; }
    public Guid? SubmissionKey { get; private set; }
    public long? SubmissionExpectedVersion { get; private set; }
    public IReadOnlyCollection<MortgageDocument> Documents => _documents.AsReadOnly();
    public IReadOnlyCollection<MortgageApplicationAuditEntry> AuditTrail => _auditTrail.AsReadOnly();
    public IReadOnlyCollection<CustomerConsent> ConsentHistory => _consentHistory.AsReadOnly();

    public static MortgageApplication Rehydrate(
        Guid id,
        long version,
        string? ownerId,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        ApplicationStatus status,
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        decimal? propertyValue,
        decimal? deposit,
        decimal? annualIncome,
        decimal? monthlyCommitments,
        EmploymentType? employmentType,
        bool creditCheckConsent,
        bool openBankingConsent,
        bool consentsRecorded,
        DecisionInPrincipleResult? decisionInPrinciple,
        string? selectedProductId,
        SelectedMortgageProductSnapshot? selectedProduct,
        IEnumerable<MortgageDocument> documents,
        IEnumerable<MortgageApplicationAuditEntry> auditTrail,
        string? createdByEmployeeId = null,
        IEnumerable<CustomerConsent>? consentHistory = null,
        Guid? submissionKey = null,
        long? submissionExpectedVersion = null)
    {
        var application = new MortgageApplication
        {
            Id = id,
            Version = version,
            OwnerId = ownerId,
            CreatedByEmployeeId = createdByEmployeeId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
            Status = status,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PhoneNumber = phoneNumber,
            PropertyValue = propertyValue,
            Deposit = deposit,
            AnnualIncome = annualIncome,
            MonthlyCommitments = monthlyCommitments,
            EmploymentType = employmentType,
            CreditCheckConsent = creditCheckConsent,
            OpenBankingConsent = openBankingConsent,
            ConsentsRecorded = consentsRecorded,
            DecisionInPrinciple = decisionInPrinciple,
            SelectedProductId = selectedProductId,
            SelectedProduct = selectedProduct,
            SubmissionKey = submissionKey,
            SubmissionExpectedVersion = submissionExpectedVersion
        };
        application._documents.AddRange(documents);
        application._auditTrail.AddRange(auditTrail);
        if (consentHistory is not null)
        {
            application._consentHistory.AddRange(consentHistory);
        }

        return application;
    }

    public static MortgageApplication CreateForEmployee(string employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            throw new ArgumentException("An employee identity is required.", nameof(employeeId));
        }

        return new MortgageApplication { CreatedByEmployeeId = employeeId };
    }

    public void RecordAudit(string actorId, string action)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("An audit event requires an actor and action.");
        }

        _auditTrail.Add(new MortgageApplicationAuditEntry(DateTime.UtcNow, actorId, action));
    }

    public void ClearAuditTrail() => _auditTrail.Clear();

    public void AssignOwner(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("An application owner is required.", nameof(ownerId));
        }

        if (OwnerId is not null && !string.Equals(OwnerId, ownerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The application already belongs to another owner.");
        }

        OwnerId = ownerId;
    }

    public void UpdateApplicant(string firstName, string lastName, string email, string phoneNumber)
    {
        EnsureEditable();
        ValidateApplicant(firstName, lastName, email, phoneNumber);
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = email.Trim();
        PhoneNumber = phoneNumber.Trim();
        Touch();
    }

    public void UpdateQualification(decimal propertyValue, decimal deposit)
    {
        EnsureEditable();
        if (propertyValue is < 50_000 or > 5_000_000 || deposit < 0 || deposit >= propertyValue)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyValue), "Enter a property value between £50,000 and £5,000,000 and a deposit below the property value.");
        }

        PropertyValue = propertyValue;
        Deposit = deposit;
        DecisionInPrinciple = null;
        SelectedProductId = null;
        SelectedProduct = null;
        Touch();
    }

    public void UpdateFinances(decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType)
    {
        EnsureEditable();
        if (annualIncome is < 10_000 or > 10_000_000 || monthlyCommitments is < 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(annualIncome), "Enter an annual income between £10,000 and £10,000,000 and monthly commitments between £0 and £100,000.");
        }

        if (!Enum.IsDefined(typeof(EmploymentType), employmentType))
        {
            throw new ArgumentOutOfRangeException(nameof(employmentType), "Select a valid employment type.");
        }

        AnnualIncome = annualIncome;
        MonthlyCommitments = monthlyCommitments;
        EmploymentType = employmentType;
        DecisionInPrinciple = null;
        Touch();
    }

    public void RecordConsents(bool creditCheckConsent, bool openBankingConsent, string termsVersion, string actorId)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(termsVersion) || string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Consent records require a terms version and actor.");
        }

        var recordedAtUtc = DateTime.UtcNow;
        CreditCheckConsent = creditCheckConsent;
        OpenBankingConsent = openBankingConsent;
        ConsentsRecorded = true;
        _consentHistory.Add(new CustomerConsent(
            ConsentPurpose.CreditCheck,
            creditCheckConsent,
            termsVersion,
            recordedAtUtc,
            actorId));
        _consentHistory.Add(new CustomerConsent(
            ConsentPurpose.OpenBanking,
            openBankingConsent,
            termsVersion,
            recordedAtUtc,
            actorId));
        DecisionInPrinciple = null;
        Touch();
    }

    public (decimal PropertyValue, decimal Deposit, decimal AnnualIncome, bool CreditCheckConsent) GetDecisionInputs()
    {
        EnsureEditable();
        if (PropertyValue is null || Deposit is null || AnnualIncome is null)
        {
            throw new InvalidOperationException("Complete your property, deposit and income details before viewing a decision in principle.");
        }

        return (PropertyValue.Value, Deposit.Value, AnnualIncome.Value, CreditCheckConsent);
    }

    public void RecordIndicativeDecision(DecisionInPrincipleResult result)
    {
        var inputs = GetDecisionInputs();
        if (result.RequestedLoanAmount != inputs.PropertyValue - inputs.Deposit ||
            string.IsNullOrWhiteSpace(result.RuleVersion) ||
            !Enum.IsDefined(result.Outcome) ||
            (inputs.CreditCheckConsent
                ? result.Outcome == DecisionInPrincipleOutcome.CreditCheckNotSelected || result.IndicativeMaximumBorrowing is null or < 0
                : result.Outcome != DecisionInPrincipleOutcome.CreditCheckNotSelected || result.IndicativeMaximumBorrowing is not null))
            throw new ArgumentException("The eligibility decision does not match the application.", nameof(result));

        DecisionInPrinciple = result;
        Touch();
    }

    public void SelectProduct(string productId, SelectedMortgageProductSnapshot product)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new ArgumentException("A mortgage product must be selected.", nameof(productId));
        }
        ArgumentNullException.ThrowIfNull(product);
        if (!string.Equals(productId, product.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("The selected product must match its saved terms.", nameof(product));
        }

        SelectedProductId = productId;
        SelectedProduct = product;
        Touch();
    }

    public void AddDocument(Guid documentId, string fileName, long sizeInBytes)
    {
        EnsureEditable();
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(fileName) || sizeInBytes <= 0)
        {
            throw new ArgumentException("A document must have an identifier, file name and positive size.");
        }

        _documents.Add(new MortgageDocument(documentId, fileName, sizeInBytes, DateTime.UtcNow));
        Touch();
    }

    public void Submit(Guid? submissionKey = null)
    {
        EnsureEditable();
        if (submissionKey == Guid.Empty)
        {
            throw new ArgumentException("A valid submission key is required.", nameof(submissionKey));
        }
        if (string.IsNullOrWhiteSpace(FirstName) ||
            string.IsNullOrWhiteSpace(LastName) ||
            string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(PhoneNumber) ||
            PropertyValue is null ||
            Deposit is null ||
            AnnualIncome is null ||
            EmploymentType is null ||
            !ConsentsRecorded ||
            DecisionInPrinciple is null ||
            SelectedProductId is null)
        {
            throw new InvalidOperationException("Complete your applicant, property, income and product details before submitting.");
        }
        if (SelectedProduct is null || !string.Equals(SelectedProductId, SelectedProduct.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Select the product again to record its terms before submitting.");
        }

        ValidateApplicant(FirstName, LastName, Email, PhoneNumber);
        if (PropertyValue is < 50_000 or > 5_000_000 || Deposit < 0 || Deposit >= PropertyValue ||
            AnnualIncome is < 10_000 or > 10_000_000 || MonthlyCommitments is null or < 0 or > 100_000 ||
            !Enum.IsDefined(EmploymentType.Value))
        {
            throw new InvalidOperationException("Review your property and income details before submitting.");
        }

        SubmissionExpectedVersion = submissionKey is null ? null : Version;
        SubmissionKey = submissionKey;
        Status = ApplicationStatus.Submitted;
        Touch();
    }

    private static void ValidateApplicant(string? firstName, string? lastName, string? email, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length is < 2 or > 60 ||
            string.IsNullOrWhiteSpace(lastName) || lastName.Trim().Length is < 2 or > 60 ||
            string.IsNullOrWhiteSpace(email) || email.Trim().Length > 254 ||
            !new EmailAddressAttribute().IsValid(email.Trim()) ||
            string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Trim().Length > 30 ||
            !new PhoneAttribute().IsValid(phoneNumber.Trim()))
        {
            throw new ArgumentException("Enter a valid name, email address and phone number.");
        }
    }

    private void EnsureEditable()
    {
        if (Status != ApplicationStatus.InProgress)
        {
            throw new InvalidOperationException("Submitted applications cannot be changed.");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        Version++;
    }
}
