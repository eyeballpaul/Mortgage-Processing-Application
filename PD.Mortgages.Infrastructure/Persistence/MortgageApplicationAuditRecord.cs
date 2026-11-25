namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class MortgageApplicationAuditRecord
{
    public long Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}
