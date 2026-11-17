namespace Tezgah.Core.Messaging;

/// <summary>An event about one company's data. Handlers run in that company's tenant scope.</summary>
public interface ITenantEvent : IIntegrationEvent
{
    Guid TenantId { get; }
}
