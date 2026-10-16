using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Queues one <see cref="PushDelivery"/> per active device of an audience — the one writer of the push outbox, shared
/// by the staff-feed decorator and the staff chat so the quiet-hours floor and the device lookup cannot drift apart.
/// </summary>
internal static class PushFanOut
{
    public static async Task QueueAsync(
        IDeviceRegistrationRepository devices,
        IPushDeliveryRepository deliveries,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        Guid clinicId,
        NotificationCategory category,
        IReadOnlyCollection<string> audience,
        Guid? appointmentId,
        Guid? staffMessageId,
        DateTime earliestUtc,
        CancellationToken cancellationToken)
    {
        if (audience.Count == 0)
        {
            return;
        }

        var active = await devices.GetActiveForUsersAsync(clinicId, audience, cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var label = StaffNotificationRules.PushLabel(category);
        // The floor is applied ONCE, here, rather than re-tested on every scan: a send time that depended on
        // when the dispatcher happened to look would release a backlog of banners at 03:00 after an outage.
        var sendNotBefore = ReminderSchedule.DeferPastQuietHours(
            earliestUtc < now ? now : earliestUtc,
            RemindersConfig.QuietHoursLocal(configuration));

        var rows = active
            .Select(device => PushDelivery.Create(
                clinicId, device.Id, device.UserId, category, label, appointmentId, sendNotBefore, now, staffMessageId))
            .ToList();

        await deliveries.AddRangeAsync(rows, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
