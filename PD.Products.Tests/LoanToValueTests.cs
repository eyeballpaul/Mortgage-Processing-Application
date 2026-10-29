using PD.Products.Domain.ValueObjects;

namespace PD.Products.Tests;

public sealed class LoanToValueTests
{
    [Fact]
    public void CalculatesPercentageFromLoanAndPropertyValue()
    {
        var loanToValue = LoanToValue.FromLoanAndPropertyValue(180_000m, 200_000m);

        Assert.Equal(90m, loanToValue.Percentage);
        Assert.True(loanToValue.IsWithinLimit(90));
        Assert.False(loanToValue.IsWithinLimit(89));
    }
}
