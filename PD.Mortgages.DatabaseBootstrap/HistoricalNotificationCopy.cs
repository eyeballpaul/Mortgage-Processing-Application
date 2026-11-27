using Microsoft.Data.SqlClient;
using System.Data;

namespace PD.Mortgages.DatabaseBootstrap;

internal static class HistoricalNotificationCopy
{
    public static async Task CopyAsync(SqlConnection source, SqlConnection target)
    {
        await source.OpenAsync();
        await target.OpenAsync();
        await using var select = source.CreateCommand();
        select.CommandText = "SELECT EventId, ApplicationId, RecordedAtUtc FROM dbo.SimulatedNotifications";
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            await using var insert = target.CreateCommand();
            insert.CommandText = """
                INSERT INTO dbo.SimulatedNotifications (EventId, ApplicationId, RecordedAtUtc)
                SELECT @eventId, @applicationId, @recordedAtUtc
                WHERE NOT EXISTS (SELECT 1 FROM dbo.SimulatedNotifications WITH (UPDLOCK, HOLDLOCK) WHERE EventId = @eventId);
                IF EXISTS (SELECT 1 FROM dbo.SimulatedNotifications WHERE EventId = @eventId AND ApplicationId <> @applicationId)
                    THROW 50001, 'Historical notification conflicts with an existing event.', 1;
                """;
            insert.Parameters.Add("@eventId", SqlDbType.UniqueIdentifier).Value = reader.GetGuid(0);
            insert.Parameters.Add("@applicationId", SqlDbType.UniqueIdentifier).Value = reader.GetGuid(1);
            insert.Parameters.Add("@recordedAtUtc", SqlDbType.DateTime2).Value = reader.GetDateTime(2);
            await insert.ExecuteNonQueryAsync();
        }
    }
}
