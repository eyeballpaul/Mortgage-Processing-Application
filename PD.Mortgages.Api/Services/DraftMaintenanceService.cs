using PD.Mortgages.Domain.Entities;
using PD.Mortgages.Domain.Repositories;

namespace PD.Mortgages.Api.Services;

public sealed class DraftMaintenanceService(
    IServiceScopeFactory scopes,
    ILogger<DraftMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan DraftInactivity = TimeSpan.FromDays(30);
    private static readonly TimeSpan CleanupRecheck = TimeSpan.FromDays(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Mortgage draft maintenance failed; it will be retried.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IMortgageDraftLifecycle>();
        var now = DateTime.UtcNow;
        var cutoff = now - DraftInactivity;

        foreach (var draft in await lifecycle.FindExpiredDraftsAsync(cutoff, BatchSize, cancellationToken))
        {
            try
            {
                await using var draftScope = scopes.CreateAsyncScope();
                var draftLifecycle = draftScope.ServiceProvider.GetRequiredService<IMortgageDraftLifecycle>();
                await draftLifecycle.TryDeleteDraftAsync(draft.Id, draft.Version,
                    new MortgageApplicationAuditEntry(now, "system:draft-retention", "Inactive draft expired"),
                    cutoff, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not expire mortgage draft {ApplicationId}.", draft.Id);
            }
        }

        foreach (var id in await lifecycle.FindDocumentsDueForCleanupAsync(now - CleanupRecheck, BatchSize, cancellationToken))
        {
            try
            {
                await using var cleanupScope = scopes.CreateAsyncScope();
                var documents = cleanupScope.ServiceProvider.GetRequiredService<IMortgageDocumentStorage>();
                var cleanupLifecycle = cleanupScope.ServiceProvider.GetRequiredService<IMortgageDraftLifecycle>();
                await documents.DeleteAllAsync(id, cancellationToken);
                await cleanupLifecycle.MarkDocumentsCleanedAsync(id, DateTime.UtcNow, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not clean documents for deleted mortgage draft {ApplicationId}.", id);
            }
        }
    }
}
