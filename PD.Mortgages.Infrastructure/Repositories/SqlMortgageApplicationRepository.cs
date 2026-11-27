using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class SqlMortgageApplicationRepository(MortgageDbContext dbContext) : IMortgageApplicationRepository, IMortgageDraftLifecycle
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Applications.AsNoTracking()
            .SingleOrDefaultAsync(application => application.Id == id && application.DeletedAtUtc == null, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<bool> TouchActivityAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.Applications
            .Where(application => application.Id == id && application.DeletedAtUtc == null &&
                application.Status == ApplicationStatus.InProgress)
            .ExecuteUpdateAsync(updates => updates.SetProperty(application => application.LastActivityAtUtc, DateTime.UtcNow), cancellationToken) > 0;

    public async Task<MortgageApplicationSearchResult> SearchAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var applications = dbContext.Applications.AsNoTracking().Where(application => application.DeletedAtUtc == null);

        if (status is not null)
        {
            applications = applications.Where(application => application.Status == status.Value);
        }

        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            applications = applications.Where(application =>
                application.IdSearchText.Contains(normalizedSearch) ||
                (application.CreatedByEmployeeId != null && application.CreatedByEmployeeId.Contains(normalizedSearch)) ||
                ((application.FirstName ?? string.Empty) + " " + (application.LastName ?? string.Empty)).Contains(normalizedSearch) ||
                (application.Email != null && application.Email.Contains(normalizedSearch)));
        }

        var totalCount = await applications.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var records = await applications
            .OrderByDescending(application => application.UpdatedAtUtc)
            .ThenByDescending(application => application.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new MortgageApplicationSearchResult(
            records.Select(ToDomain).ToList(),
            totalCount,
            page,
            pageSize);
    }

    public async Task AddAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        dbContext.Applications.Add(ToRecord(application));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(
        MortgageApplication application,
        MortgageApplicationAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Applications.Add(ToRecord(application));
        dbContext.AuditEntries.Add(ToAuditRecord(application.Id, auditEntry));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default) =>
        UpdateCoreAsync(application, null, cancellationToken);

    public Task UpdateAsync(
        MortgageApplication application,
        MortgageApplicationAuditEntry auditEntry,
        CancellationToken cancellationToken = default) =>
        UpdateCoreAsync(application, auditEntry, cancellationToken);

    private async Task UpdateCoreAsync(
        MortgageApplication application,
        MortgageApplicationAuditEntry? auditEntry,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var record = await dbContext.Applications
            .SingleOrDefaultAsync(stored => stored.Id == application.Id && stored.DeletedAtUtc == null, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");

        if (record.Version + 1 != application.Version)
        {
            throw new InvalidOperationException("This application was changed elsewhere. Reload it before saving your changes.");
        }

        var submitted = record.Status == ApplicationStatus.InProgress && application.Status == ApplicationStatus.Submitted;
        var updatedRecord = ToRecord(application);
        record.Version = updatedRecord.Version;
        record.IdSearchText = updatedRecord.IdSearchText;
        record.Status = updatedRecord.Status;
        record.UpdatedAtUtc = updatedRecord.UpdatedAtUtc;
        record.LastActivityAtUtc = DateTime.UtcNow;
        record.CreatedByEmployeeId = updatedRecord.CreatedByEmployeeId;
        record.FirstName = updatedRecord.FirstName;
        record.LastName = updatedRecord.LastName;
        record.Email = updatedRecord.Email;
        record.SnapshotJson = updatedRecord.SnapshotJson;
        if (auditEntry is not null)
        {
            dbContext.AuditEntries.Add(ToAuditRecord(application.Id, auditEntry));
        }
        if (submitted)
        {
            dbContext.SubmissionOutbox.Add(new SubmissionOutboxRecord
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                OccurredAtUtc = application.UpdatedAtUtc
            });
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("This application was changed elsewhere. Reload it before saving your changes.", exception);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Applications
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);
        if (record is null)
        {
            return;
        }

        dbContext.Applications.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryDeleteDraftAsync(Guid id, long version, MortgageApplicationAuditEntry auditEntry,
        DateTime? inactiveBeforeUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var candidates = dbContext.Applications.Where(application =>
            application.Id == id && application.Version == version &&
            application.DeletedAtUtc == null && application.Status == ApplicationStatus.InProgress);
        if (inactiveBeforeUtc is not null)
        {
            candidates = candidates.Where(application => application.LastActivityAtUtc < inactiveBeforeUtc.Value);
        }

        var now = DateTime.UtcNow;
        var deleted = await candidates.ExecuteUpdateAsync(updates => updates
            .SetProperty(application => application.Version, application => application.Version + 1)
            .SetProperty(application => application.DeletedAtUtc, now)
            .SetProperty(application => application.UpdatedAtUtc, now)
            .SetProperty(application => application.FirstName, (string?)null)
            .SetProperty(application => application.LastName, (string?)null)
            .SetProperty(application => application.Email, (string?)null)
            .SetProperty(application => application.CreatedByEmployeeId, (string?)null)
            .SetProperty(application => application.SnapshotJson, "{}"), cancellationToken);
        if (deleted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.AuditEntries.Add(ToAuditRecord(id, auditEntry));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ExpiredMortgageDraft>> FindExpiredDraftsAsync(
        DateTime inactiveBeforeUtc, int limit, CancellationToken cancellationToken = default) =>
        await dbContext.Applications.AsNoTracking()
            .Where(application => application.DeletedAtUtc == null &&
                application.Status == ApplicationStatus.InProgress && application.LastActivityAtUtc < inactiveBeforeUtc)
            .OrderBy(application => application.LastActivityAtUtc)
            .Take(limit)
            .Select(application => new ExpiredMortgageDraft(application.Id, application.Version))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> FindDocumentsDueForCleanupAsync(
        DateTime cleanedBeforeUtc, int limit, CancellationToken cancellationToken = default) =>
        await dbContext.Applications.AsNoTracking()
            .Where(application => application.DeletedAtUtc != null &&
                (application.DocumentsCleanedAtUtc == null || application.DocumentsCleanedAtUtc < cleanedBeforeUtc))
            .OrderBy(application => application.DocumentsCleanedAtUtc)
            .ThenBy(application => application.Id)
            .Take(limit)
            .Select(application => application.Id)
            .ToListAsync(cancellationToken);

    public async Task MarkDocumentsCleanedAsync(Guid id, DateTime cleanedAtUtc, CancellationToken cancellationToken = default) =>
        await dbContext.Applications
            .Where(application => application.Id == id && application.DeletedAtUtc != null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(application => application.DocumentsCleanedAtUtc, cleanedAtUtc), cancellationToken);

    private static MortgageApplicationRecord ToRecord(MortgageApplication application)
    {
        var snapshot = MortgageApplicationSnapshot.FromDomain(application);
        return new MortgageApplicationRecord
        {
            Id = application.Id,
            Version = application.Version,
            IdSearchText = application.Id.ToString("D"),
            Status = application.Status,
            UpdatedAtUtc = application.UpdatedAtUtc,
            LastActivityAtUtc = application.UpdatedAtUtc,
            CreatedByEmployeeId = application.CreatedByEmployeeId,
            FirstName = application.FirstName,
            LastName = application.LastName,
            Email = application.Email,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions)
        };
    }

    private static MortgageApplicationAuditRecord ToAuditRecord(Guid applicationId, MortgageApplicationAuditEntry entry) => new()
    {
        ApplicationId = applicationId,
        RecordedAtUtc = entry.RecordedAtUtc,
        ActorId = entry.ActorId,
        Action = entry.Action
    };

    private static MortgageApplication ToDomain(MortgageApplicationRecord record)
    {
        var snapshot = JsonSerializer.Deserialize<MortgageApplicationSnapshot>(record.SnapshotJson, JsonOptions)
            ?? throw new InvalidDataException($"Mortgage application '{record.Id}' could not be read.");
        return snapshot.ToDomain();
    }
}
