using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Queues OS push alongside the in-app feed, by <b>decorating</b> <see cref="INotificationGenerator"/>.
///
/// <para><b>A decorator and not twelve edited call sites.</b> One hook reaches every category the generator has or
/// will have, so a notification added later cannot be the one that silently never pushes — the
/// <c>fixes-dont-propagate</c> shape this repository records as its dominant defect. It also means the fan-out
/// cannot see a category it has not classified: <see cref="StaffNotificationRules.ReachesALockedPhone"/> throws on
/// an unknown one.</para>
///
/// <para><b>The feed always wins.</b> Every method awaits the inner generator first and fans out afterwards inside
/// a swallow-and-log — the whole chain is already a post-commit best-effort side effect of a clinical or financial
/// operation that has committed, so a push failure must not fail, delay or roll back it (AC-55), and must not cost
/// the in-app notification either.</para>
///
/// <para>⚠️ It lives in Infrastructure rather than beside <see cref="NotificationGenerator"/> because it reads the
/// operator's quiet-hours window from configuration — the same reason <c>ReminderScheduler</c>, the other
/// post-commit best-effort writer implementing an Application interface, lives here.</para>
/// </summary>
public sealed class PushNotificationGeneratorDecorator : INotificationGenerator
{
    private readonly INotificationGenerator _inner;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IDeviceRegistrationRepository _devices;
    private readonly IPushDeliveryRepository _deliveries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOsPushAvailability _availability;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PushNotificationGeneratorDecorator> _logger;

    public PushNotificationGeneratorDecorator(
        INotificationGenerator inner,
        IUserRepository users,
        IDoctorRepository doctors,
        IDeviceRegistrationRepository devices,
        IPushDeliveryRepository deliveries,
        IUnitOfWork unitOfWork,
        IOsPushAvailability availability,
        IConfiguration configuration,
        ILogger<PushNotificationGeneratorDecorator> logger)
    {
        _inner = inner;
        _users = users;
        _doctors = doctors;
        _devices = devices;
        _deliveries = deliveries;
        _unitOfWork = unitOfWork;
        _availability = availability;
        _configuration = configuration;
        _logger = logger;
    }

    // ---- The five categories that reach a locked phone (AC-43) -------------------------------------

    public async Task AppointmentCreatedAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await _inner.AppointmentCreatedAsync(
            clinicId, appointmentId, actorUserId, patientName, appointmentDateTimeUtc, cancellationToken);

        await FanOutAsync(
            clinicId, NotificationCategory.AppointmentCreated, actorUserId, targetUserId: null,
            appointmentId, DateTime.UtcNow, cancellationToken);
    }

    public async Task ScheduleAppointmentReminderAsync(
        Guid clinicId, Guid appointmentId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await _inner.ScheduleAppointmentReminderAsync(
            clinicId, appointmentId, patientName, appointmentDateTimeUtc, cancellationToken);

        // The generator schedules nothing inside the lead window — the « created » notification already covers
        // that case — so mirroring the condition is what keeps a push from announcing a feed row that does not
        // exist. Both sides read the due time from one rule.
        var dueTime = StaffNotificationRules.ReminderDueTimeUtc(appointmentDateTimeUtc);
        if (dueTime <= DateTime.UtcNow)
        {
            return;
        }

        await FanOutAsync(
            clinicId, NotificationCategory.Reminder, actorUserId: null, targetUserId: null,
            appointmentId, dueTime, cancellationToken);
    }

    public async Task AppointmentCancelledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await _inner.AppointmentCancelledAsync(
            clinicId, appointmentId, actorUserId, patientName, appointmentDateTimeUtc, cancellationToken);

        // The queued reminder push for this appointment is deliberately left in place rather than deleted:
        // PushDispatchJob re-checks the appointment at send time and fails it there, which also covers the
        // reschedule race a delete here could not.
        await FanOutAsync(
            clinicId, NotificationCategory.AppointmentCancelled, actorUserId, targetUserId: null,
            appointmentId, DateTime.UtcNow, cancellationToken);
    }

    public async Task AppointmentRescheduledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName,
        DateTime oldDateTimeUtc, DateTime newDateTimeUtc, CancellationToken cancellationToken = default)
    {
        await _inner.AppointmentRescheduledAsync(
            clinicId, appointmentId, actorUserId, patientName, oldDateTimeUtc, newDateTimeUtc, cancellationToken);

        await FanOutAsync(
            clinicId, NotificationCategory.AppointmentRescheduled, actorUserId, targetUserId: null,
            appointmentId, DateTime.UtcNow, cancellationToken);
    }

    public async Task EnsurePostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, Guid? doctorId, string patientName, DateTime appointmentEndUtc,
        CancellationToken cancellationToken = default)
    {
        await _inner.EnsurePostVisitReviewAsync(
            clinicId, appointmentId, doctorId, patientName, appointmentEndUtc, cancellationToken);

        // Resolved through the same rule the generator targets its feed row with, so the banner and the row it
        // announces cannot reach different people.
        var targetUserId = await StaffNotificationRules.ResolveDoctorUserIdAsync(
            _doctors, clinicId, doctorId, cancellationToken);

        await FanOutAsync(
            clinicId, NotificationCategory.PostVisitReview, actorUserId: null, targetUserId,
            appointmentId, appointmentEndUtc, cancellationToken);
    }

    // ---- The seven that stay in the app (AC-44) ----------------------------------------------------

    public Task LowStockAsync(
        Guid clinicId, Guid stockItemId, string itemName, int currentStock, int minimumStockLevel,
        CancellationToken cancellationToken = default) =>
        _inner.LowStockAsync(clinicId, stockItemId, itemName, currentStock, minimumStockLevel, cancellationToken);

    public Task EnsureStockExpiringSoonAsync(
        Guid clinicId, Guid stockItemId, string itemName, DateTime earliestExpiryUtc,
        CancellationToken cancellationToken = default) =>
        _inner.EnsureStockExpiringSoonAsync(clinicId, stockItemId, itemName, earliestExpiryUtc, cancellationToken);

    public Task ClearStockExpiringSoonAsync(
        Guid clinicId, Guid stockItemId, CancellationToken cancellationToken = default) =>
        _inner.ClearStockExpiringSoonAsync(clinicId, stockItemId, cancellationToken);

    public Task EnsureBackupStaleAsync(
        Guid clinicId, DateTime? lastSuccessUtc, int staleAfterHours, CancellationToken cancellationToken = default) =>
        _inner.EnsureBackupStaleAsync(clinicId, lastSuccessUtc, staleAfterHours, cancellationToken);

    public Task ClearBackupStaleAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        _inner.ClearBackupStaleAsync(clinicId, cancellationToken);

    // AC-3.6 — never a lock-screen banner. Pass-through only, and `ReachesALockedPhone` says so a second time for
    // the reader: an accounting reminder spending the OS's single notification permission is how the five
    // time-critical categories lose it.
    public Task EnsureSubscriptionWarningAsync(
        Guid clinicId, int thresholdDays, DateTime endsOn, CancellationToken cancellationToken = default) =>
        _inner.EnsureSubscriptionWarningAsync(clinicId, thresholdDays, endsOn, cancellationToken);

    public Task ClearSubscriptionWarningsAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        _inner.ClearSubscriptionWarningsAsync(clinicId, cancellationToken);

    // Same pass-through, same reason (vendor-whatsapp-messaging-quota AC-3.4): a quota notice is not time-critical
    // to a person, and `StaffNotificationRules.ReachesALockedPhone` answers `false` for the category so nothing here
    // could queue one even if this method tried.
    public Task EnsureMessagingAllowanceWarningAsync(
        Guid clinicId, string monthKey, int thresholdPercent, int allowance, DateTime resetsOn,
        CancellationToken cancellationToken = default) =>
        _inner.EnsureMessagingAllowanceWarningAsync(
            clinicId, monthKey, thresholdPercent, allowance, resetsOn, cancellationToken);

    public Task ClearMessagingAllowanceWarningsAsync(
        Guid clinicId, string? keepMonthKey, IReadOnlyCollection<int> keepThresholds,
        CancellationToken cancellationToken = default) =>
        _inner.ClearMessagingAllowanceWarningsAsync(clinicId, keepMonthKey, keepThresholds, cancellationToken);

    public Task CancelPostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, CancellationToken cancellationToken = default) =>
        _inner.CancelPostVisitReviewAsync(clinicId, appointmentId, cancellationToken);

    public Task ReminderDeliveryFailedAsync(
        Guid clinicId, Guid? appointmentId, string patientName, string channel, string? reason,
        bool patientRequiresRecontact, CancellationToken cancellationToken = default) =>
        _inner.ReminderDeliveryFailedAsync(
            clinicId, appointmentId, patientName, channel, reason, patientRequiresRecontact, cancellationToken);

    // Pass-through, like its neighbours above: `StaffNotificationRules.ReachesALockedPhone` answers false for
    // this category, so there is nothing to fan out. Enrolling again happens at a keyboard on the next
    // sign-in, which a lock-screen banner cannot make any more actionable.
    public Task SecondFactorResetAsync(
        Guid clinicId,
        string targetUserId,
        SecondFactorResetBy by,
        CancellationToken cancellationToken = default) =>
        _inner.SecondFactorResetAsync(clinicId, targetUserId, by, cancellationToken);

    // Pass-through for the same reason as the one above: not a locked-phone category.
    public Task SessionEndedForReplayAsync(
        Guid clinicId, string targetUserId, string? deviceLabel, CancellationToken cancellationToken = default) =>
        _inner.SessionEndedForReplayAsync(clinicId, targetUserId, deviceLabel, cancellationToken);

    // Pass-through: the export has already completed by the time this is written, so there is nothing a banner
    // could let anybody intervene in.
    // Pass-through: ArchiveStale is in-app only (StaffNotificationRules.ReachesALockedPhone answers false), and
    // what it asks for — download a multi-gigabyte file — cannot be done from a lock screen.
    public Task EnsureArchiveStaleAsync(
        Guid clinicId, DateTime? lastDownloadedUtc, int staleAfterDays,
        CancellationToken cancellationToken = default) =>
        _inner.EnsureArchiveStaleAsync(clinicId, lastDownloadedUtc, staleAfterDays, cancellationToken);

    public Task ClearArchiveStaleAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        _inner.ClearArchiveStaleAsync(clinicId, cancellationToken);

    public Task ClinicArchiveExportedAsync(
        Guid clinicId, string actorUserId, string actorName, CancellationToken cancellationToken = default) =>
        _inner.ClinicArchiveExportedAsync(clinicId, actorUserId, actorName, cancellationToken);

    // ---- The fan-out ------------------------------------------------------------------------------

    /// <summary>
    /// One queued row per active device of the audience. Never throws.
    /// </summary>
    private async Task FanOutAsync(
        Guid clinicId,
        NotificationCategory category,
        string? actorUserId,
        string? targetUserId,
        Guid? appointmentId,
        DateTime effectiveFeedTimeUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!StaffNotificationRules.ReachesALockedPhone(category) || !_availability.IsAvailableAtAll)
            {
                return;
            }

            var audience = await AudienceAsync(clinicId, actorUserId, targetUserId, cancellationToken);
            if (audience.Count == 0)
            {
                return;
            }

            var devices = await _devices.GetActiveForUsersAsync(clinicId, audience, cancellationToken);
            if (devices.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var label = StaffNotificationRules.PushLabel(category);
            // The floor is applied ONCE, here, rather than re-tested on every scan: a send time that depended on
            // when the dispatcher happened to look would release a backlog of banners at 03:00 after an outage.
            var sendNotBefore = ReminderSchedule.DeferPastQuietHours(
                effectiveFeedTimeUtc < now ? now : effectiveFeedTimeUtc,
                RemindersConfig.QuietHoursLocal(_configuration));

            var rows = devices
                .Select(device => PushDelivery.Create(
                    clinicId, device.Id, device.UserId, category, label, appointmentId, sendNotBefore, now))
                .ToList();

            await _deliveries.AddRangeAsync(rows, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to queue OS push for {Category} in clinic {ClinicId}", category, clinicId);
        }
    }

    /// <summary>
    /// Who this notification is for — the in-app feed's own rule (AC-45): the actor never, a targeted row only its
    /// target, otherwise the whole clinic.
    ///
    /// <para>⚠️ <b>Inactive accounts are excluded, and that is the one place this departs from the feed's SQL</b> —
    /// which does not test <c>IsActive</c> because it does not need to: a deactivated account is refused on every
    /// request, so it can never open the app and read the feed. Its <i>device</i> is a different matter. A banner
    /// on a former employee's phone is the difference that would be visible, and their registration stays active
    /// until they sign out — which someone who was deactivated will not do.</para>
    /// </summary>
    private async Task<IReadOnlyList<string>> AudienceAsync(
        Guid clinicId, string? actorUserId, string? targetUserId, CancellationToken cancellationToken)
    {
        if (targetUserId != null)
        {
            return string.Equals(targetUserId, actorUserId, StringComparison.Ordinal)
                ? Array.Empty<string>()
                : new[] { targetUserId };
        }

        // paging: null = every member, the first-class case the paging primitive models — an audience is not a page.
        var staff = await _users.GetByClinicIdAsync(clinicId, null, null, cancellationToken);

        return staff.Items
            .Where(u => u.IsActive && !string.Equals(u.Id, actorUserId, StringComparison.Ordinal))
            .Select(u => u.Id)
            .ToList();
    }
}
