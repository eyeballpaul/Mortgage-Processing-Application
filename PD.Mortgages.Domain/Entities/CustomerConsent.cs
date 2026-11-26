namespace PD.Mortgages.Domain.Entities;

public enum ConsentPurpose
{
    CreditCheck,
    OpenBanking
}

public sealed record CustomerConsent(
    ConsentPurpose Purpose,
    bool Granted,
    string TermsVersion,
    DateTime RecordedAtUtc,
    string ActorId);
