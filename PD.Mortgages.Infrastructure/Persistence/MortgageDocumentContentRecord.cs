namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class MortgageDocumentContentRecord
{
    public Guid ApplicationId { get; set; }
    public Guid DocumentId { get; set; }
    public byte[] Contents { get; set; } = [];
}
