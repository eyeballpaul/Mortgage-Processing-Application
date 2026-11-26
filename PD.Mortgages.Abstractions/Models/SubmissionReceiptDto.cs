namespace PD.Mortgages.Application.Models;

/// <summary>Confirms submission without exposing the application details.</summary>
public sealed record SubmissionReceiptDto(Guid Reference, DateTimeOffset SubmittedAt);
