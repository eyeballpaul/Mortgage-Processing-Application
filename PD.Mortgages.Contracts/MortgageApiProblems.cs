namespace PD.Mortgages.Contracts;

public static class MortgageApiProblems
{
    public const string ApplicationUnavailable = "urn:pd-mortgages:application-unavailable";
    public const string ApplicationNotFound = "urn:pd-mortgages:application-not-found";
    public const string DocumentNotFound = "urn:pd-mortgages:document-not-found";
    public const string ReceiptNotFound = "urn:pd-mortgages:receipt-not-found";
    public const string ProductNotFound = "urn:pd-mortgages:product-not-found";
    public const string ConcurrencyConflict = "urn:pd-mortgages:concurrency-conflict";
}
