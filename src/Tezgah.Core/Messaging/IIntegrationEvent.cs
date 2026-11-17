namespace Tezgah.Core.Messaging;

/// <summary>A fact one module tells the others about, delivered through the outbox. See <see cref="ITenantEvent"/>.</summary>
public interface IIntegrationEvent;

/// <summary>
/// Delivery is at-least-once: a handler can see the same event again after a crash or a failed
/// sibling handler, so it must check state before acting.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
