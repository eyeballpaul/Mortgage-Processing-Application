using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.ValueObjects;

namespace PD.Mortgages.Domain.Services;

public sealed class MortgageEligibilityService
{
    public IReadOnlyList<MortgageProduct> GetEligibleProducts(
        decimal propertyValue,
        decimal deposit,
        IReadOnlyList<MortgageProduct> products)
    {
        if (propertyValue <= 0 || deposit < 0 || deposit >= propertyValue)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyValue), "Enter a valid property value and deposit.");
        }

        var loanToValue = LoanToValue.FromLoanAndPropertyValue(propertyValue - deposit, propertyValue);
        return products.Where(product => loanToValue.IsWithinLimit(product.MaximumLoanToValue)).ToList();
    }
}
