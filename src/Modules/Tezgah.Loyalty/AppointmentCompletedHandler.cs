using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Contracts;
using Tezgah.Core.Messaging;

namespace Tezgah.Loyalty;

/// <summary>
/// A completed appointment earns a stamp in every program that counts appointments. The dispatcher
/// only calls this for companies that have the loyalty module.
/// </summary>
internal sealed class AppointmentCompletedHandler(LoyaltyDbContext db, CardDesk desk)
    : IIntegrationEventHandler<AppointmentCompleted>
{
    public async Task HandleAsync(AppointmentCompleted appointment, CancellationToken ct)
    {
        var programIds = await db.Programs
            .Where(program => program.IsActive && program.EarnRule == EarnRule.Appointment)
            .Select(program => program.Id)
            .ToListAsync(ct);

        foreach (var programId in programIds)
        {
            // The source makes redelivery of the same event a no-op.
            await desk.StampAsync(
                programId, appointment.CustomerId, count: 1, $"appointment:{appointment.AppointmentId}",
                appointment.ServiceName, stampedBy: "Randevu", ct);
        }
    }
}
