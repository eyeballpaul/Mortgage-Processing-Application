namespace PD.Mortgages.Application.Models;

public sealed record MortgageDocumentDto(
    Guid Id,
    string FileName,
    long SizeInBytes,
    DateTime UploadedAtUtc);
