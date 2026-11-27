namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class SubmissionOutboxRecord
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
}
