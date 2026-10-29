using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Tests;

public sealed class MortgageApplicationTests
{
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
            application.GenerateIndicativeDecisionInPrinciple());

        Assert.Contains("property", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateIndicativeDecisionUsesIncomeMultipleWhenCreditConsentIsRecorded()
    {
        var application = new MortgageApplication();
        application.UpdateQualification(300_000m, 30_000m);
        application.UpdateFinances(100_000m, 1_000m, EmploymentType.Employed);
        application.RecordConsents(true, false);

        var result = application.GenerateIndicativeDecisionInPrinciple();

        Assert.Equal(DecisionInPrincipleOutcome.WithinIndicativeRange, result.Outcome);
        Assert.Equal(270_000m, result.RequestedLoanAmount);
        Assert.Equal(450_000m, result.IndicativeMaximumBorrowing);
    }

    [Fact]
    public void SubmitRequiresASelectedProduct()
    {
        var application = new MortgageApplication();
        application.UpdateQualification(300_000m, 30_000m);
        application.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
        application.UpdateFinances(100_000m, 1_000m, EmploymentType.Employed);
        application.RecordConsents(true, true);
        application.GenerateIndicativeDecisionInPrinciple();

        Assert.Throws<InvalidOperationException>(() => application.Submit());
    }

    [Fact]
    public void SelectProductRejectsAnEmptyProductId()
    {
        var application = new MortgageApplication();

        Assert.Throws<ArgumentException>(() => application.SelectProduct(" "));
    }

    [Fact]
    public void SubmittedApplicationCannotBeChanged()
    {
        var application = new MortgageApplication();
        application.UpdateQualification(300_000m, 30_000m);
        application.UpdateApplicant("Jane", "Doe", "jane@example.com", "07123456789");
        application.UpdateFinances(100_000m, 1_000m, EmploymentType.Employed);
        application.RecordConsents(true, true);
        application.GenerateIndicativeDecisionInPrinciple();
        application.SelectProduct("fixed-2");
        application.Submit();

        Assert.Throws<InvalidOperationException>(() => application.UpdateQualification(400_000m, 40_000m));
    }
}
