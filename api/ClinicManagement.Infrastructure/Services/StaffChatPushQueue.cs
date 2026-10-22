using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The phone half of a chat alert (staff-chat Part 7): one queued push per device of everyone the message rings.
///
/// <para>⚠️ <b>It waits <see cref="Grace"/> before it may leave</b>, and <c>PushDispatchJob</c> re-reads the
/// recipient's read marker then: a colleague at the desk who reads the message on the PC must not also have their
/// phone buzz for it. The lock screen gets « Nouveau message » and nothing else — the text never leaves the app.</para>
/// </summary>
public sealed class StaffChatPushQueue : IStaffChatPushQueue
{
    /// <summary>How long a message has to be read somewhere before the phone is told — short, or it arrives late.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);

    private readonly IDeviceRegistrationRepository _devices;
    private readonly IPushDeliveryRepository _deliveries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOsPushAvailability _availability;
    private readonly IConfiguration _configuration;
    private readonly IPushDispatchWake _wake;
    private readonly ILogger<StaffChatPushQueue> _logger;

    public StaffChatPushQueue(
        IDeviceRegistrationRepository devices,
        IPushDeliveryRepository deliveries,
        IUnitOfWork unitOfWork,
        IOsPushAvailability availability,
        IConfiguration configuration,
        IPushDispatchWake wake,
        ILogger<StaffChatPushQueue> logger)
    {
        _devices = devices;
        _deliveries = deliveries;
        _unitOfWork = unitOfWork;
        _availability = availability;
        _configuration = configuration;
        _wake = wake;
        _logger = logger;
    }

    public async Task QueueAsync(
        StaffMessage message, IReadOnlyCollection<string> recipients, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_availability.IsAvailableAtAll)
            {
                return;
            }

            var audience = recipients.Where(id => id != message.SenderUserId).Distinct().ToList();
            await PushFanOut.QueueAsync(
                _devices, _deliveries, _unitOfWork, _configuration, message.ClinicId,
                NotificationCategory.StaffChatMessage, audience, appointmentId: null, message.Id,
                message.SentAt + Grace, cancellationToken);
            // Leave as soon as the grace is over, not at the next minutely tick.
            _wake.Request(message.SentAt + Grace + TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Staff chat message {MessageId} saved but its phone notification was not queued", message.Id);
        }
    }
}
