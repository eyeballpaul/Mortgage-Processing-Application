using System.Text.Json;

namespace PD.Mortgages.Events;

internal sealed record SubmissionMessage(Guid EventId, Guid ApplicationId, string EventType, int Version, DateTime OccurredAtUtc)
{
    public const string Type = "ApplicationSubmitted";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static bool TryRead(BinaryData body, string messageId, out SubmissionMessage? message)
    {
        try
        {
            message = JsonSerializer.Deserialize<SubmissionMessage>(body.ToMemory().Span, JsonOptions);
            return message is not null && message.EventId != Guid.Empty && message.ApplicationId != Guid.Empty &&
                message.EventType == Type && message.Version == 1 && message.OccurredAtUtc != default &&
                message.OccurredAtUtc.Kind == DateTimeKind.Utc &&
                message.EventId.ToString("D") == messageId;
        }
        catch (JsonException)
        {
            message = null;
            return false;
        }
    }
}
