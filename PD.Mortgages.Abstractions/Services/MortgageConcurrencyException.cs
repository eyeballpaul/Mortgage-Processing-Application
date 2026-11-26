namespace PD.Mortgages.Application.Services;

public sealed class MortgageConcurrencyException : Exception
{
    public const string ConflictMessage = "This application was changed elsewhere. Reload it before saving your changes.";

    public MortgageConcurrencyException() : base(ConflictMessage) { }
    public MortgageConcurrencyException(Exception innerException) : base(ConflictMessage, innerException) { }
}
