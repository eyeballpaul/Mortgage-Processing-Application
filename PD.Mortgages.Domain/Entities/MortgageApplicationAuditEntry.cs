namespace PD.Mortgages.Domain.Entities;

public sealed record MortgageApplicationAuditEntry(DateTime RecordedAtUtc, string ActorId, string Action);
