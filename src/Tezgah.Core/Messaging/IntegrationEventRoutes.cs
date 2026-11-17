using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Tezgah.Core.Messaging;

/// <summary>Maps a stored event type name to the handlers subscribed to it, and the module each belongs to.</summary>
public sealed class IntegrationEventRoutes
{
    private readonly Dictionary<string, List<Route>> _routes = [];

    internal delegate Task Handle(IServiceProvider services, object integrationEvent, CancellationToken ct);

    internal sealed record Route(string ModuleKey, Type EventType, Handle Handle);

    public static string NameOf<TEvent>()
        where TEvent : ITenantEvent => typeof(TEvent).FullName!;

    public void Add<TEvent, THandler>(string moduleKey)
        where TEvent : ITenantEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var name = NameOf<TEvent>();
        if (!_routes.TryGetValue(name, out var routes))
        {
            _routes[name] = routes = [];
        }

        routes.Add(new Route(moduleKey, typeof(TEvent), (services, integrationEvent, ct) =>
            services.GetRequiredService<THandler>().HandleAsync((TEvent)integrationEvent, ct)));
    }

    internal IReadOnlyList<Route> For(string eventType) =>
        _routes.TryGetValue(eventType, out var routes) ? routes : [];

    internal static ITenantEvent Deserialize(Route route, string payload) =>
        (ITenantEvent)(JsonSerializer.Deserialize(payload, route.EventType, OutboxMessage.Json)
            ?? throw new InvalidOperationException($"Outbox payload for '{route.EventType.Name}' is empty."));
}
