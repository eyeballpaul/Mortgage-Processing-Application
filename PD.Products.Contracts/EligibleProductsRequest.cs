namespace PD.Products.Contracts;

public sealed record EligibleProductsRequest(
    decimal PropertyValue,
    decimal Deposit);
