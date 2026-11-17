using System.Text.Json;

namespace Tezgah.Core.Messaging;

/// <summary>
/// An event waiting to be delivered. It is inserted in the same transaction as the state change
/// it describes, so "the change happened" and "the event will be sent" cannot disagree.
/// </summary>
public sealed class OutboxMessage
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage From<TEvent>(TEvent integrationEvent)
        where TEvent : ITenantEvent => new()
        {
            Id = Guid.CreateVersion7(),
            Type = IntegrationEventRoutes.NameOf<TEvent>(),
            Payload = JsonSerializer.Serialize(integrationEvent, Json),
            OccurredAt = DateTimeOffset.UtcNow,
        };

    internal void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
    }

    internal void MarkFailed(string error)
    {
        Attempts++;
        LastError = error;
    }
}
