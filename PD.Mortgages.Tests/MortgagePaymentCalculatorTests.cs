using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Tests;

public sealed class MortgagePaymentCalculatorTests
{
    [Fact]
    public void ZeroInterestReturnsPrincipalDividedByNumberOfMonths()
    {
        var payment = MortgagePaymentCalculator.MonthlyPayment(120_000m, 0m, 10);

        Assert.Equal(1_000m, payment);
    }

    [Fact]
    public void InvalidLoanOrTermReturnsZero()
    {
        Assert.Equal(0m, MortgagePaymentCalculator.MonthlyPayment(0m, 4m));
        Assert.Equal(0m, MortgagePaymentCalculator.MonthlyPayment(100_000m, 4m, 0));
    }

    [Fact]
    public void PositiveInterestReturnsRoundedPositivePayment()
    {
        var payment = MortgagePaymentCalculator.MonthlyPayment(270_000m, 4.29m);

        Assert.True(payment > 0);
        Assert.Equal(2, decimal.Round(payment, 2).ToString("F2").Split('.')[1].Length);
    }
}
