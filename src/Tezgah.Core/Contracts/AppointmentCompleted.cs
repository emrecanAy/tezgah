using Tezgah.Core.Messaging;

namespace Tezgah.Core.Contracts;

/// <summary>
/// Published by Appointments when a visit took place. Loyalty listens, if the company has it,
/// to stamp the customer's card without the two modules knowing each other.
/// </summary>
public sealed record AppointmentCompleted(
    Guid TenantId, Guid AppointmentId, Guid CustomerId, string ServiceName, DateTimeOffset CompletedAt) : ITenantEvent;
