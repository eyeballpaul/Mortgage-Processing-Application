using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Web.Models;

namespace PD.Mortgages.Web.Services;

public sealed class WizardViewModelFactory
{
    public QualificationViewModel CreateQualification(MortgageApplicationDto application) => new()
    {
        PropertyValue = application.PropertyValue,
        Deposit = application.Deposit
    };

    public ApplicantViewModel CreateApplicant(MortgageApplicationDto application) => new()
    {
        FirstName = application.FirstName ?? "",
        LastName = application.LastName ?? "",
        Email = application.Email ?? "",
        PhoneNumber = application.PhoneNumber ?? ""
    };

    public FinancesViewModel CreateFinances(MortgageApplicationDto application) => new()
    {
        AnnualIncome = application.AnnualIncome,
        MonthlyCommitments = application.MonthlyCommitments,
        EmploymentType = application.EmploymentType
    };

    public ConnectionsViewModel CreateConnections(MortgageApplicationDto application) => new()
    {
        CreditCheckConsent = application.CreditCheckConsent,
        OpenBankingConsent = application.OpenBankingConsent
    };

    public ProductsViewModel CreateProducts(
        MortgageApplicationDto application,
        IReadOnlyList<MortgageProductDto> products) => new()
    {
        SelectedProductId = application.SelectedProductId,
        LoanAmount = (application.PropertyValue ?? 0) - (application.Deposit ?? 0),
        DipOutcome = application.DecisionInPrinciple?.Outcome,
        Products = products
    };

    public DashboardViewModel CreateDashboard(
        MortgageApplicationDto application,
        MortgageProductDto? product,
        int currentStep) => new()
    {
        Application = application,
        Product = product,
        CurrentStep = currentStep,
        EstimatedMonthlyPayment = CalculatePayment(application, product)
    };

    public ReviewViewModel CreateReview(
        MortgageApplicationDto application,
        MortgageProductDto? product) => new()
    {
        Application = application,
        Product = product,
        EstimatedMonthlyPayment = CalculatePayment(application, product)
    };

    private static decimal? CalculatePayment(MortgageApplicationDto application, MortgageProductDto? product)
    {
        if (product is null)
        {
            return null;
        }

        var loanAmount = (application.PropertyValue ?? 0) - (application.Deposit ?? 0);
        return MortgagePaymentCalculator.MonthlyPayment(loanAmount, product.InterestRate);
    }
}
