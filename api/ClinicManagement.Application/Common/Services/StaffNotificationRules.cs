using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// The facts about a staff notification that <b>two</b> writers now need: the in-app feed
/// (<see cref="NotificationGenerator"/>) and the OS-push fan-out that decorates it.
///
/// <para>It exists because each of these was a private detail of the generator, and the fan-out needs the same
/// answer. A second copy of « the reminder fires 24 h before » would mean a banner arriving at a different hour
/// from the feed row it announces — visible to a user, invisible to every test that mocks one side.</para>
/// </summary>
public static class StaffNotificationRules
{
    /// <summary>A reminder fires this far before the appointment; nothing is scheduled inside the window.</summary>
    public static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    /// <summary>When the ~24 h reminder becomes due. The feed row's effective time and the push's earliest send.</summary>
    public static DateTime ReminderDueTimeUtc(DateTime appointmentDateTimeUtc) =>
        appointmentDateTimeUtc - ReminderLeadTime;

    /// <summary>
    /// The five categories that reach a locked phone (AC-43), and the four that deliberately do not (AC-44).
    ///
    /// <para>The line is <b>time-critical to a person</b>, not importance: a patient is arriving, or has just
    /// left. Low stock, an expiring box, a stale backup and a failed reminder are all real and all wait for
    /// somebody to be at the desk — waking a dentist at home for a box of gloves is how an OS notification
    /// permission gets revoked, and revoking it costs the four that matter.</para>
    /// </summary>
    public static bool ReachesALockedPhone(NotificationCategory category) => category switch
    {
        NotificationCategory.AppointmentCreated => true,
        NotificationCategory.AppointmentCancelled => true,
        NotificationCategory.AppointmentRescheduled => true,
        NotificationCategory.Reminder => true,
        NotificationCategory.PostVisitReview => true,
        NotificationCategory.LowStock => false,
        NotificationCategory.StockExpiringSoon => false,
        NotificationCategory.BackupStale => false,
        NotificationCategory.ReminderFailed => false,
        // AC-3.6: an accounting reminder is not time-critical to a person, and spending the OS's single
        // notification permission on one risks losing the five categories that are.
        NotificationCategory.SubscriptionExpiring => false,
        // AC-3.4: a forfait running low waits for somebody at the desk, and the person who can act on it is the
        // vendor rather than whoever is holding the phone.
        NotificationCategory.MessagingAllowanceLow => false,
        // A new category does not silently start pushing. Deciding is the point — a default of `true` would put
        // an unreviewed message on a lock screen, and `false` would look like a decision nobody made.
        _ => throw new ArgumentOutOfRangeException(
            nameof(category), category, "Cette catégorie ne dit pas si elle doit produire une notification système.")
    };

    /// <summary>
    /// The <b>whole</b> of what a push carries besides its routing ids (AC-47) — a fixed French phrase per
    /// category, identical to the feed row's own title.
    ///
    /// <para>⚠️ « Identical to the feed row's title » is held by <c>PushFanOutTests</c>, which compares this
    /// against the <see cref="Domain.Entities.StaffNotification"/> the generator actually wrote, rather than
    /// against a retyped table. That is the only way the two can be *shown* not to drift; a constant here that
    /// merely looks right would still be a second authority.</para>
    /// </summary>
    public static string PushLabel(NotificationCategory category) => category switch
    {
        NotificationCategory.AppointmentCreated => "Nouveau rendez-vous",
        NotificationCategory.AppointmentCancelled => "Rendez-vous annulé",
        NotificationCategory.AppointmentRescheduled => "Rendez-vous reporté",
        NotificationCategory.Reminder => "Rappel de rendez-vous",
        NotificationCategory.PostVisitReview => "Compte rendu de visite",
        _ => throw new ArgumentOutOfRangeException(
            nameof(category), category, "Cette catégorie ne produit pas de notification système.")
    };

    /// <summary>
    /// The user a doctor-targeted notification belongs to: the appointment's <c>DoctorId</c> → its linked
    /// <c>User</c>. Any miss — no id, unknown doctor, another clinic's doctor, or a doctor with no linked account
    /// — resolves to <c>null</c>, meaning « all staff ».
    ///
    /// <para>The cross-clinic degradation is not politeness: the feed read filters on clinic <b>and</b> target
    /// user, so a foreign doctor's user id would make the notification visible to nobody at all.</para>
    /// </summary>
    public static async Task<string?> ResolveDoctorUserIdAsync(
        IDoctorRepository doctors, Guid clinicId, Guid? doctorId, CancellationToken cancellationToken)
    {
        if (doctorId is null)
        {
            return null;
        }

        var doctor = await doctors.GetByIdAsync(doctorId.Value, cancellationToken);
        return doctor == null || doctor.ClinicId != clinicId ? null : doctor.UserId;
    }
}
