using Microsoft.EntityFrameworkCore;
using PD.Mortgages.Infrastructure.Persistence;

namespace PD.Mortgages.Api.Services;

internal sealed class SimulatedNotificationStore(MortgageDbContext db)
{
    public async Task<bool> RecordAsync(SubmissionMessage message, CancellationToken cancellationToken)
    {
        var recordedAt = DateTime.UtcNow;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [SimulatedNotifications] ([EventId], [ApplicationId], [RecordedAtUtc])
            SELECT {message.EventId}, {message.ApplicationId}, {recordedAt}
            WHERE NOT EXISTS (
                SELECT 1 FROM [SimulatedNotifications] WITH (UPDLOCK, HOLDLOCK)
                WHERE [EventId] = {message.EventId})
            """, cancellationToken);
        if (inserted > 0) return true;

        var existingApplicationId = await db.SimulatedNotifications.AsNoTracking()
            .Where(entry => entry.EventId == message.EventId)
            .Select(entry => entry.ApplicationId)
            .SingleAsync(cancellationToken);
        if (existingApplicationId != message.ApplicationId)
            throw new InvalidDataException("Event ID already belongs to another application.");
        return false;
    }
}
