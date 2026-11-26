using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Tests;

public sealed class MortgageApplicationTests
{
    [Theory]
    [InlineData("", "Doe", "jane@example.com", "07123456789")]
    [InlineData("Jane", "D", "jane@example.com", "07123456789")]
    [InlineData("Jane", "Doe", "not-an-email", "07123456789")]
    [InlineData("Jane", "Doe", "jane@example.com", "not-a-phone")]
    public void ApplicantDetailsMustBeValid(string firstName, string lastName, string email, string phone)
    {
        var application = new MortgageApplication();
        Assert.Throws<ArgumentException>(() => application.UpdateApplicant(firstName, lastName, email, phone));
        Assert.Null(application.FirstName);
        Assert.Equal(0, application.Version);
    }

    [Fact]
    public void FinancialInputsMustStayWithinSupportedRanges()
    {
        var application = new MortgageApplication();
        Assert.Throws<ArgumentOutOfRangeException>(() => application.UpdateQualification(49_999, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => application.UpdateQualification(5_000_001, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => application.UpdateFinances(9_999, 0, EmploymentType.Employed));
        Assert.Throws<ArgumentOutOfRangeException>(() => application.UpdateFinances(10_000, 100_001, EmploymentType.Employed));
        Assert.Equal(0, application.Version);
    }

    [Fact]
    public void EnumDisplayNamesUseReadableLabelsForCompoundValues()
    {
        Assert.Equal("In progress", ApplicationStatus.InProgress.GetDisplayName());
        Assert.Equal("Awaiting information", ApplicationStatus.AwaitingInformation.GetDisplayName());
        Assert.Equal("Approved in principle", ApplicationStatus.ApprovedInPrinciple.GetDisplayName());
        Assert.Equal("Self-employed", EmploymentType.SelfEmployed.GetDisplayName());
        Assert.Equal("Credit check not selected", DecisionInPrincipleOutcome.CreditCheckNotSelected.GetDisplayName());
        Assert.Equal("Within indicative range", DecisionInPrincipleOutcome.WithinIndicativeRange.GetDisplayName());
        Assert.Equal("Above indicative range", DecisionInPrincipleOutcome.AboveIndicativeRange.GetDisplayName());
    }

    [Fact]
    public void UpdateQualificationRejectsDepositAtOrAbovePropertyValue()
    {
        var application = new MortgageApplication();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            application.UpdateQualification(200_000m, 200_000m));

        Assert.Contains("deposit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateIndicativeDecisionRequiresQualificationAndIncome()
    {
        var application = new MortgageApplication();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            application.GetDecisionInputs());

        Assert.Contains("property", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordIndicativeDecisionPreservesServiceResult()
    {
        var application = new MortgageApplication();
        application.UpdateQualification(300_000m, 30_000m);
        application.UpdateFinances(100_000m, 1_000m, EmploymentType.Employed);
        application.RecordConsents(true, false, "terms-1", "customer-1");

        TestEligibilityClient.Record(application);
        var result = application.DecisionInPrinciple!;

        Assert.Equal(DecisionInPrincipleOutcome.WithinIndicativeRange, result.Outcome);
        Assert.Equal(270_000m, result.RequestedLoanAmount);
        Assert.Equal(450_000m, result.IndicativeMaximumBorrowing);
    }

    [Fact]
    public void ExistingDecisionWithoutRuleVersionCanBeRead()
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<DecisionInPrincipleResult>(
            """{"Outcome":0,"RequestedLoanAmount":270000,"IndicativeMaximumBorrowing":null,"AssessedAtUtc":"2026-01-01T00:00:00Z"}""");
        Assert.NotNull(result);
        Assert.Null(result.RuleVersion);
        Assert.Equal(270000, result.RequestedLoanAmount);
    }

    [Fact]
    public void SubmitRequiresASelectedProduct()
    {
        var application = new MortgageApplication();
        application.UpdateQualification(300_000m, 30_000m);
        application.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
        application.UpdateFinances(100_000m, 1_000m, EmploymentType.Employed);
        application.RecordConsents(true, true, "terms-1", "customer-1");
        TestEligibilityClient.Record(application);

        Assert.Throws<InvalidOperationException>(() => application.Submit());
    }

    [Fact]
    public void SelectProductRejectsAnEmptyProductId()
    {
        var application = new MortgageApplication();

        Assert.Throws<ArgumentException>(() => application.SelectProduct(" ", Product()));
        Assert.Throws<ArgumentException>(() => application.SelectProduct("other", Product()));
    }

    [Fact]
    public void LegacyDraftRequiresProductSnapshotBeforeSubmission()
    {
        var application = new MortgageApplication();
        application.UpdateQualification(300_000m, 30_000m);
        application.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
        application.UpdateFinances(100_000m, 1_000m, EmploymentType.Employed);
        application.RecordConsents(true, true, "terms-1", "customer-1");
        TestEligibilityClient.Record(application);
        application.SelectProduct("fixed-2", Product());
        var legacy = (MortgageApplicationSnapshot.FromDomain(application) with { SelectedProduct = null }).ToDomain();

        Assert.Throws<InvalidOperationException>(() => legacy.Submit());
        legacy.SelectProduct("fixed-2", Product());
        legacy.Submit();
        Assert.Equal(ApplicationStatus.Submitted, legacy.Status);
    }

    [Fact]
    public void ApplicationOwnerCannotBeChanged()
    {
        var application = new MortgageApplication();
        application.AssignOwner("customer-1");

        Assert.Throws<InvalidOperationException>(() => application.AssignOwner("customer-2"));
    }

    [Fact]
    public void EmployeeCreatedApplicationRequiresAndStoresEmployeeIdentity()
    {
        var application = MortgageApplication.CreateForEmployee("staff-1");

        Assert.Equal("staff-1", application.CreatedByEmployeeId);
        Assert.Null(application.OwnerId);
        Assert.Throws<ArgumentException>(() => MortgageApplication.CreateForEmployee(" "));
    }

    [Fact]
    public void SubmittedApplicationCannotBeChanged()
    {
        var application = new MortgageApplication();
        application.UpdateQualification(300_000m, 30_000m);
        application.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
        application.UpdateFinances(100_000m, 1_000m, EmploymentType.Employed);
        application.RecordConsents(true, true, "terms-1", "customer-1");
        TestEligibilityClient.Record(application);
        application.SelectProduct("fixed-2", Product());
        application.Submit();

        Assert.Throws<InvalidOperationException>(() => application.UpdateQualification(400_000m, 40_000m));
    }

    private static SelectedMortgageProductSnapshot Product() =>
        new("fixed-2", "Fixed", 4m, 999m, 5m, 2, 90, "original");

    [Fact]
    public void ConsentHistoryRetainsEachPurposeDecisionVersionAndActor()
    {
        var application = new MortgageApplication();

        application.RecordConsents(true, false, "terms-1", "staff-42");
        application.RecordConsents(false, true, "terms-2", "customer-17");

        Assert.Equal(4, application.ConsentHistory.Count);
        Assert.Collection(
            application.ConsentHistory,
            creditConsent =>
            {
                Assert.Equal(ConsentPurpose.CreditCheck, creditConsent.Purpose);
                Assert.True(creditConsent.Granted);
                Assert.Equal("terms-1", creditConsent.TermsVersion);
                Assert.Equal("staff-42", creditConsent.ActorId);
            },
            openBankingConsent =>
            {
                Assert.Equal(ConsentPurpose.OpenBanking, openBankingConsent.Purpose);
                Assert.False(openBankingConsent.Granted);
                Assert.Equal("terms-1", openBankingConsent.TermsVersion);
            },
            creditConsent =>
            {
                Assert.Equal(ConsentPurpose.CreditCheck, creditConsent.Purpose);
                Assert.False(creditConsent.Granted);
                Assert.Equal("terms-2", creditConsent.TermsVersion);
                Assert.Equal("customer-17", creditConsent.ActorId);
            },
            openBankingConsent =>
            {
                Assert.Equal(ConsentPurpose.OpenBanking, openBankingConsent.Purpose);
                Assert.True(openBankingConsent.Granted);
                Assert.Equal("terms-2", openBankingConsent.TermsVersion);
            });
    }
}
