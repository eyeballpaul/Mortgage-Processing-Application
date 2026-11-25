using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Domain.Repositories;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Infrastructure.Repositories;

public sealed class SqlMortgageApplicationRepository(MortgageDbContext dbContext) : IMortgageApplicationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MortgageApplication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Applications.AsNoTracking()
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<MortgageApplicationSearchResult> SearchAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var applications = dbContext.Applications.AsNoTracking().AsQueryable();

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

    public async Task UpdateAsync(MortgageApplication application, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Applications
            .SingleOrDefaultAsync(stored => stored.Id == application.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mortgage application '{application.Id}' was not found.");

        if (record.Version + 1 != application.Version)
        {
            throw new InvalidOperationException("This application was changed elsewhere. Reload it before saving your changes.");
        }

        var updatedRecord = ToRecord(application);
        record.Version = updatedRecord.Version;
        record.IdSearchText = updatedRecord.IdSearchText;
        record.Status = updatedRecord.Status;
        record.UpdatedAtUtc = updatedRecord.UpdatedAtUtc;
        record.CreatedByEmployeeId = updatedRecord.CreatedByEmployeeId;
        record.FirstName = updatedRecord.FirstName;
        record.LastName = updatedRecord.LastName;
        record.Email = updatedRecord.Email;
        record.SnapshotJson = updatedRecord.SnapshotJson;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
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
            CreatedByEmployeeId = application.CreatedByEmployeeId,
            FirstName = application.FirstName,
            LastName = application.LastName,
            Email = application.Email,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions)
        };
    }

    private static MortgageApplication ToDomain(MortgageApplicationRecord record)
    {
        var snapshot = JsonSerializer.Deserialize<MortgageApplicationSnapshot>(record.SnapshotJson, JsonOptions)
            ?? throw new InvalidDataException($"Mortgage application '{record.Id}' could not be read.");
        return snapshot.ToDomain();
    }
}
