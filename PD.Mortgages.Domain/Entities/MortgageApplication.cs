using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Domain.Entities;

public sealed class MortgageApplication
{
    private readonly List<MortgageDocument> _documents = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
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
    public IReadOnlyCollection<MortgageDocument> Documents => _documents.AsReadOnly();

    public void UpdateApplicant(string firstName, string lastName, string email, string phoneNumber)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PhoneNumber = phoneNumber;
        Touch();
    }

    public void UpdateQualification(decimal propertyValue, decimal deposit)
    {
        if (propertyValue <= 0 || deposit < 0 || deposit >= propertyValue)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyValue), "Enter a property value above zero and a deposit below the property value.");
        }

        PropertyValue = propertyValue;
        Deposit = deposit;
        DecisionInPrinciple = null;
        SelectedProductId = null;
        Touch();
    }

    public void UpdateFinances(decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType)
    {
        if (annualIncome <= 0 || monthlyCommitments < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(annualIncome), "Income must be positive and monthly commitments cannot be negative.");
        }

        AnnualIncome = annualIncome;
        MonthlyCommitments = monthlyCommitments;
        EmploymentType = employmentType;
        DecisionInPrinciple = null;
        Touch();
    }

    public void RecordConsents(bool creditCheckConsent, bool openBankingConsent)
    {
        CreditCheckConsent = creditCheckConsent;
        OpenBankingConsent = openBankingConsent;
        ConsentsRecorded = true;
        DecisionInPrinciple = null;
        Touch();
    }

    public DecisionInPrincipleResult GenerateIndicativeDecisionInPrinciple()
    {
        if (PropertyValue is null || Deposit is null || AnnualIncome is null)
        {
            throw new InvalidOperationException("Complete your property, deposit and income details before viewing a decision in principle.");
        }

        var requestedLoanAmount = PropertyValue.Value - Deposit.Value;
        var outcome = CreditCheckConsent
            ? DecisionInPrincipleOutcome.WithinIndicativeRange
            : DecisionInPrincipleOutcome.CreditCheckNotSelected;
        decimal? indicativeMaximumBorrowing = null;

        if (CreditCheckConsent)
        {
            indicativeMaximumBorrowing = AnnualIncome.Value * 4.5m;
            if (requestedLoanAmount > indicativeMaximumBorrowing.Value)
            {
                outcome = DecisionInPrincipleOutcome.AboveIndicativeRange;
            }
        }

        DecisionInPrinciple = new DecisionInPrincipleResult(
            outcome,
            requestedLoanAmount,
            indicativeMaximumBorrowing,
            DateTime.UtcNow);
        Touch();
        return DecisionInPrinciple;
    }

    public void SelectProduct(string productId)
    {
        SelectedProductId = productId;
        Touch();
    }

    public void AddDocument(Guid documentId, string fileName, long sizeInBytes)
    {
        _documents.Add(new MortgageDocument(documentId, fileName, sizeInBytes, DateTime.UtcNow));
        Touch();
    }

    public void Submit()
    {
        if (string.IsNullOrWhiteSpace(FirstName) ||
            string.IsNullOrWhiteSpace(LastName) ||
            string.IsNullOrWhiteSpace(Email) ||
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

        Status = ApplicationStatus.Submitted;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
