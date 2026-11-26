using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class MortgageApplicationRecord
{
    public Guid Id { get; set; }
    public long Version { get; set; }
    public string IdSearchText { get; set; } = string.Empty;
    public ApplicationStatus Status { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime? DocumentsCleanedAtUtc { get; set; }
    public string? CreatedByEmployeeId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
}
