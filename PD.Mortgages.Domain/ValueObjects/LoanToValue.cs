namespace PD.Mortgages.Domain.ValueObjects;

public readonly record struct LoanToValue
{
    private LoanToValue(decimal percentage) => Percentage = percentage;

    public decimal Percentage { get; }

    public static LoanToValue FromLoanAndPropertyValue(decimal loanAmount, decimal propertyValue)
    {
        if (propertyValue <= 0 || loanAmount < 0 || loanAmount > propertyValue)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyValue), "Loan and property values must form a valid loan-to-value ratio.");
        }

        return new LoanToValue(loanAmount / propertyValue * 100);
    }

    public bool IsWithinLimit(decimal maximumPercentage) => Percentage <= maximumPercentage;
}
