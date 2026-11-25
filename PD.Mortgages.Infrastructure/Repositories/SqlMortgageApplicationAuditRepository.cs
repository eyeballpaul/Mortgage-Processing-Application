using Microsoft.EntityFrameworkCore;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class SqlMortgageApplicationAuditRepository(MortgageDbContext dbContext) : IMortgageApplicationAuditRepository
{
    public async Task AppendAsync(Guid applicationId, MortgageApplicationAuditEntry entry, CancellationToken cancellationToken = default)
    {
        dbContext.AuditEntries.Add(ToRecord(applicationId, entry));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AppendLegacyAsync(
        Guid applicationId,
        IReadOnlyCollection<MortgageApplicationAuditEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var existingEntries = await dbContext.AuditEntries.AsNoTracking()
            .Where(record => record.ApplicationId == applicationId)
            .Select(record => new MortgageApplicationAuditEntry(record.RecordedAtUtc, record.ActorId, record.Action))
            .ToListAsync(cancellationToken);
        var knownEntries = existingEntries.ToHashSet();
        var missingEntries = entries
            .Where(knownEntries.Add)
            .Select(entry => ToRecord(applicationId, entry))
            .ToList();

        if (missingEntries.Count == 0)
        {
            return;
        }

        dbContext.AuditEntries.AddRange(missingEntries);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MortgageApplicationAuditPage> GetPageAsync(
        Guid applicationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var entries = dbContext.AuditEntries.AsNoTracking()
            .Where(entry => entry.ApplicationId == applicationId);
        var totalCount = await entries.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var records = await entries
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var events = records
            .Select(entry => new MortgageApplicationAuditEntry(entry.RecordedAtUtc, entry.ActorId, entry.Action))
            .ToList();

        return new MortgageApplicationAuditPage(events, totalCount, page, pageSize);
    }

    private static MortgageApplicationAuditRecord ToRecord(Guid applicationId, MortgageApplicationAuditEntry entry) => new()
    {
        ApplicationId = applicationId,
        RecordedAtUtc = entry.RecordedAtUtc,
        ActorId = entry.ActorId,
        Action = entry.Action
    };
}
