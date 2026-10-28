namespace PD.Mortgages.Domain.Entities;

public sealed record MortgageDocument(Guid Id, string FileName, long SizeInBytes, DateTime UploadedAtUtc);
