namespace PD.Mortgages.Application.Services;

public static class MortgagePaymentCalculator
{
    public static decimal MonthlyPayment(decimal loanAmount, decimal annualInterestRate, int termYears = 25)
    {
        if (loanAmount <= 0 || termYears <= 0)
        {
            return 0;
        }

        var months = termYears * 12;
        var monthlyRate = (double)(annualInterestRate / 100m / 12m);
        var principal = (double)loanAmount;
        var payment = monthlyRate == 0
            ? principal / months
            : principal * monthlyRate / (1 - Math.Pow(1 + monthlyRate, -months));

        return decimal.Round((decimal)payment, 2);
    }
}
