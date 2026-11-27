namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class SimulatedNotificationRecord
{
    public Guid EventId { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
