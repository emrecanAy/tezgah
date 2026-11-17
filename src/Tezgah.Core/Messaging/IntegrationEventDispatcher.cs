using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tezgah.Core.Modules;
using Tezgah.Core.Tenancy;

namespace Tezgah.Core.Messaging;

public sealed class IntegrationEventDispatcher(IServiceScopeFactory scopes, IOptions<IntegrationEventRoutes> routes)
{
    /// <summary>
    /// Runs each subscribed handler in its own scope, inside the event's tenant. A handler is
    /// skipped when its module is not enabled for that company: an appointment completed at a
    /// firm without the loyalty module must not create loyalty data for it.
    /// </summary>
    public async Task DispatchAsync(string eventType, string payload, CancellationToken ct)
    {
        foreach (var route in routes.Value.For(eventType))
        {
            var integrationEvent = IntegrationEventRoutes.Deserialize(route, payload);

            await using var scope = scopes.CreateAsyncScope();
            var modules = scope.ServiceProvider.GetRequiredService<ITenantModules>();
            if (!await modules.IsEnabledAsync(integrationEvent.TenantId, route.ModuleKey, ct))
            {
                continue;
            }

            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(integrationEvent.TenantId);
            await route.Handle(scope.ServiceProvider, integrationEvent, ct);
        }
    }
}
