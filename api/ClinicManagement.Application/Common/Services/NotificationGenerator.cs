using System.Globalization;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Writes in-app staff notifications (see <see cref="INotificationGenerator"/>). Persists on the caller's
/// scoped DbContext <b>after</b> the caller has already committed the core change, then broadcasts the
/// <c>"notifications"</c> realtime key. Best-effort: each public method wraps its work in try/catch and
/// swallows so a notification failure can never fail/roll back the core operation — but logs at Error with
/// the exception so a genuine bug stays visible (per the InternetProbe learning).
/// </summary>
public class NotificationGenerator : INotificationGenerator
{
    private const string RealtimeResourceKey = "notifications";

    // The lead time and the doctor→user resolution moved to StaffNotificationRules when the OS-push fan-out
    // (Part 6) became a second writer needing the same answers. A private copy here would mean a banner arriving
    // at a different hour from the feed row it announces.
    private static readonly TimeSpan ReminderLeadTime = StaffNotificationRules.ReminderLeadTime;

    // The app is Tunisia-targeted; appointment date/times are stored UTC but read best in local time.
    // The conversion itself lives in ClinicClock (AC-P6.1) — this class used to carry its own private copy.
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");

    private readonly IStaffNotificationRepository _notifications;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<NotificationGenerator> _logger;

    public NotificationGenerator(
        IStaffNotificationRepository notifications,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtimeNotifier,
        ILogger<NotificationGenerator> logger)
    {
        _notifications = notifications;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task AppointmentCreatedAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.AppointmentCreated,
                "Nouveau rendez-vous",
                $"Nouveau rendez-vous pour {patientName} le {FormatFr(appointmentDateTimeUtc)}.",
                DateTime.UtcNow,
                NotificationTargetKind.Appointment,
                actorUserId: actorUserId,
                appointmentId: appointmentId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // immediately visible → refetch is worthwhile
        }, cancellationToken);
    }

    public async Task ScheduleAppointmentReminderAsync(
        Guid clinicId, Guid appointmentId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var dueTime = appointmentDateTimeUtc - ReminderLeadTime;
            // <24h out → the "appointment created" notification suffices; schedule nothing.
            if (dueTime <= DateTime.UtcNow)
            {
                return false;
            }

            var reminder = BuildReminder(clinicId, appointmentId, patientName, appointmentDateTimeUtc, dueTime);
            await _notifications.AddAsync(reminder, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // The reminder is future-dated (surfaces only once due), so nothing is visible in any feed
            // yet — no client refetch needed now.
            return false;
        }, cancellationToken);
    }

    public async Task AppointmentCancelledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.AppointmentCancelled,
                "Rendez-vous annulé",
                $"Le rendez-vous de {patientName} du {FormatFr(appointmentDateTimeUtc)} a été annulé.",
                DateTime.UtcNow,
                NotificationTargetKind.Appointment,
                actorUserId: actorUserId,
                appointmentId: appointmentId);
            await _notifications.AddAsync(notification, cancellationToken);

            // Suppress any pending reminder — a cancelled appointment must never remind.
            var reminder = await _notifications.GetReminderByAppointmentAsync(appointmentId, cancellationToken);
            if (reminder != null)
            {
                await _notifications.RemoveAsync(reminder, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // the cancellation notice is immediately visible
        }, cancellationToken);
    }

    public async Task AppointmentRescheduledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName,
        DateTime oldDateTimeUtc, DateTime newDateTimeUtc, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.AppointmentRescheduled,
                "Rendez-vous reporté",
                $"Le rendez-vous de {patientName} a été reporté du {FormatFr(oldDateTimeUtc)} au {FormatFr(newDateTimeUtc)}.",
                DateTime.UtcNow,
                NotificationTargetKind.Appointment,
                actorUserId: actorUserId,
                appointmentId: appointmentId);
            await _notifications.AddAsync(notification, cancellationToken);

            // Move the reminder to reflect the new time so no stale old-time reminder appears.
            var newDueTime = newDateTimeUtc - ReminderLeadTime;
            var reminder = await _notifications.GetReminderByAppointmentAsync(appointmentId, cancellationToken);
            if (reminder != null)
            {
                if (newDueTime > DateTime.UtcNow)
                {
                    reminder.MoveReminder(newDueTime, ReminderTitle, ReminderMessage(patientName, newDateTimeUtc));
                }
                else
                {
                    // New time is within the lead window → no reminder should exist anymore.
                    await _notifications.RemoveAsync(reminder, cancellationToken);
                }
            }
            else if (newDueTime > DateTime.UtcNow)
            {
                await _notifications.AddAsync(
                    BuildReminder(clinicId, appointmentId, patientName, newDateTimeUtc, newDueTime), cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // the reschedule notice is immediately visible
        }, cancellationToken);
    }

    public async Task LowStockAsync(
        Guid clinicId, Guid stockItemId, string itemName, int currentStock, int minimumStockLevel,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.LowStock,
                "Stock faible",
                $"Stock faible : {itemName} ({currentStock}/{minimumStockLevel}).",
                DateTime.UtcNow,
                NotificationTargetKind.StockItem,
                actorUserId: null, // no single actor → visible to all staff
                stockItemId: stockItemId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // immediately visible to all staff
        }, cancellationToken);
    }

    public async Task EnsureStockExpiringSoonAsync(
        Guid clinicId, Guid stockItemId, string itemName, DateTime earliestExpiryUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var title = StockExpiringSoonTitle;
            var message = StockExpiringSoonMessage(itemName, earliestExpiryUtc, DateTime.UtcNow);

            var existing = await _notifications.GetStockExpiringSoonByItemAsync(stockItemId, cancellationToken);
            if (existing != null)
            {
                // Already flagged. Restate only when the batch it is about actually changed — matched on the
                // item+date prefix, not the whole message, so the daily countdown alone is not a change.
                if (existing.Message.StartsWith(StockExpiringSoonKey(itemName, earliestExpiryUtc), StringComparison.Ordinal))
                {
                    return false;
                }

                existing.Restate(title, message);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return true;
            }

            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.StockExpiringSoon,
                title, message,
                DateTime.UtcNow,
                NotificationTargetKind.StockItem,
                actorUserId: null, // nobody "did" an expiry → visible to all staff, like low stock
                stockItemId: stockItemId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // immediately visible to all staff
        }, cancellationToken);
    }

    public async Task ClearStockExpiringSoonAsync(
        Guid clinicId, Guid stockItemId, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var existing = await _notifications.GetStockExpiringSoonByItemAsync(stockItemId, cancellationToken);
            if (existing == null)
            {
                return false; // nothing flagged — the overwhelmingly common case on a daily scan
            }

            await _notifications.RemoveAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // the row left the feed → clients refetch
        }, cancellationToken);
    }

    public async Task EnsureBackupStaleAsync(
        Guid clinicId, DateTime? lastSuccessUtc, int staleAfterHours,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var title = BackupStaleTitle;
            var message = BackupStaleMessage(lastSuccessUtc, staleAfterHours);

            var existing = await _notifications.GetBackupStaleAsync(clinicId, cancellationToken);
            if (existing != null)
            {
                // Already flagged. Restate only when the *fact* changed — matched on the stable prefix, not the
                // whole message, exactly as the expiry alert does: the message carries an elapsed count that
                // ticks up every day, and comparing the whole thing would turn this ensure into a daily
                // broadcast (the churn the pair exists to avoid).
                if (existing.Message.StartsWith(BackupStaleKey(lastSuccessUtc), StringComparison.Ordinal))
                {
                    return false;
                }

                existing.Restate(title, message);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return true;
            }

            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.BackupStale,
                title, message,
                DateTime.UtcNow,
                NotificationTargetKind.BackupSettings,
                actorUserId: null, // nobody "did" a staleness → visible to all staff, like low stock and expiry
                stockItemId: null);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task ClearBackupStaleAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var existing = await _notifications.GetBackupStaleAsync(clinicId, cancellationToken);
            if (existing == null)
            {
                return false; // nothing flagged — the common case on a clinic that backs up every night
            }

            await _notifications.RemoveAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // the row left the feed → clients refetch
        }, cancellationToken);
    }

    public async Task EnsureSubscriptionWarningAsync(
        Guid clinicId, int thresholdDays, DateTime endsOn, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var title = SubscriptionWarningTitle(thresholdDays);
            var message = SubscriptionWarningMessage(endsOn);

            // ⚠️ Rows for the OTHER thresholds are withdrawn whenever they name a superseded date, and that is what
            // keeps the bell coherent when EndsOn moves *within* the warning window rather than out of it: a grant
            // of five days on a « 1 jour restant » cabinet writes a « 7 jours » row and used to leave the old one in
            // place, so the feed asserted two different end dates at once. The mirror case is a cancellation moving
            // the date closer. Message equality is date equality here — it carries the end date and nothing else.
            var withdrawn = await WithdrawStaleSubscriptionWarningsAsync(
                clinicId, thresholdDays, message, cancellationToken);

            var existing = await _notifications.GetSubscriptionWarningAsync(
                clinicId, thresholdDays, cancellationToken);
            if (existing != null)
            {
                // This threshold has already been announced, so no second row (AC-3.5). Unlike the two ensure
                // pairs above, the whole message is compared rather than a prefix: it carries no countdown, only
                // the end date, so it is stable day to day and differs exactly when a grant has moved the date.
                if (string.Equals(existing.Message, message, StringComparison.Ordinal))
                {
                    return withdrawn;
                }

                existing.Restate(title, message);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return true;
            }

            var notification = StaffNotification.ForSubscription(
                Guid.NewGuid(), clinicId, title, message, DateTime.UtcNow, thresholdDays);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // a genuinely new unread row → it badges the bell, which is the point (AC-3.4)
        }, cancellationToken);
    }

    /// <summary>
    /// Removes this cabinet's outstanding warnings for <i>other</i> thresholds that name a different end date.
    /// Returns whether anything left the feed, so the caller can broadcast even when its own row is unchanged.
    /// </summary>
    private async Task<bool> WithdrawStaleSubscriptionWarningsAsync(
        Guid clinicId, int thresholdDays, string currentMessage, CancellationToken cancellationToken)
    {
        var outstanding = await _notifications.GetSubscriptionWarningsAsync(clinicId, cancellationToken);
        var stale = outstanding
            .Where(n => n.SubscriptionThresholdDays != thresholdDays
                        && !string.Equals(n.Message, currentMessage, StringComparison.Ordinal))
            .ToList();

        if (stale.Count == 0)
        {
            return false;
        }

        foreach (var notification in stale)
        {
            await _notifications.RemoveAsync(notification, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ClearSubscriptionWarningsAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var existing = await _notifications.GetSubscriptionWarningsAsync(clinicId, cancellationToken);
            if (existing.Count == 0)
            {
                return false; // nothing outstanding — the common case for a cabinet nowhere near its date
            }

            foreach (var notification in existing)
            {
                await _notifications.RemoveAsync(notification, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // the rows left the feed → clients refetch
        }, cancellationToken);
    }

    public async Task EnsurePostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, Guid? doctorId, string patientName, DateTime appointmentEndUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var targetUserId = await ResolveTargetUserIdAsync(clinicId, doctorId, cancellationToken);
            var title = PostVisitReviewTitle;
            var message = PostVisitReviewMessage(patientName);

            var existing = await _notifications.GetPostVisitReviewByAppointmentAsync(appointmentId, cancellationToken);
            if (existing != null)
            {
                existing.MovePostVisitReview(appointmentEndUtc, targetUserId, title, message);
            }
            else
            {
                var notification = new StaffNotification(
                    Guid.NewGuid(), clinicId, NotificationCategory.PostVisitReview,
                    title, message,
                    appointmentEndUtc, // effective feed time = appointment end; surfaces only once it passes
                    NotificationTargetKind.Appointment,
                    actorUserId: null, // it is a prompt TO the doctor, so no actor is excluded
                    appointmentId: appointmentId,
                    stockItemId: null,
                    targetUserId: targetUserId);
                await _notifications.AddAsync(notification, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // Future-dated (visible only once the end time passes) → no client refetch needed unless the
            // end is already in the past (appointment created/updated with an end already behind us).
            return appointmentEndUtc <= DateTime.UtcNow;
        }, cancellationToken);
    }

    public async Task CancelPostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var existing = await _notifications.GetPostVisitReviewByAppointmentAsync(appointmentId, cancellationToken);
            if (existing == null)
            {
                return false;
            }

            var wasVisible = existing.EffectiveFeedTime <= DateTime.UtcNow;
            await _notifications.RemoveAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return wasVisible; // only make clients refetch if it was actually showing
        }, cancellationToken);
    }

    public async Task ReminderDeliveryFailedAsync(
        Guid clinicId, Guid? appointmentId, string patientName, string channel, string? reason,
        bool patientRequiresRecontact, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var isRecall = appointmentId is null;
            var what = isRecall ? "La relance" : "Le rappel de rendez-vous";
            var why = string.IsNullOrWhiteSpace(reason) ? null : $" ({reason.Trim()})";
            var recontact = patientRequiresRecontact
                ? " Ce patient doit être recontacté."
                : string.Empty;

            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.ReminderFailed,
                isRecall ? "Relance non envoyée" : "Rappel non envoyé",
                $"{what} de {patientName} par {channel} n'a pas pu être envoyé{why}.{recontact}",
                DateTime.UtcNow,
                isRecall ? NotificationTargetKind.Recall : NotificationTargetKind.Appointment,
                actorUserId: null, // no actor to exclude — whoever is at the desk must see it (AC-P3.8)
                appointmentId: appointmentId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // immediately visible → refetch is worthwhile
        }, cancellationToken);
    }

    public async Task ClinicArchiveExportedAsync(
        Guid clinicId, string actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var who = string.IsNullOrWhiteSpace(actorName) ? "Un administrateur" : actorName.Trim();

            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.ClinicArchiveExported,
                "Archive du cabinet exportée",
                $"{who} a téléchargé une archive complète du cabinet : l'ensemble des dossiers patients, des "
                + "documents et de la comptabilité, dans un seul fichier non chiffré. Si vous n'êtes pas au "
                + "courant de cette exportation, prévenez immédiatement votre administrateur.",
                DateTime.UtcNow,
                NotificationTargetKind.Security,
                // Clinic-wide (no target) with the actor excluded — see the interface on why this is the one
                // security notice that is not targeted.
                actorUserId: actorUserId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task SecondFactorResetAsync(
        Guid clinicId, string targetUserId, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.SecondFactorReset,
                "Second facteur réinitialisé",
                "Un administrateur a réinitialisé votre second facteur. Vous devrez en enrôler un nouveau à "
                + "votre prochaine connexion. Si vous n'êtes pas à l'origine de cette demande, prévenez "
                + "immédiatement votre administrateur.",
                DateTime.UtcNow,
                NotificationTargetKind.Security,
                // ⚠️ A TARGET and no actor exclusion. The one person who must see this is the affected user —
                // broadcasting « le second facteur de X a été réinitialisé » to the whole practice would
                // publish a security event about one colleague to everybody.
                actorUserId: null,
                targetUserId: targetUserId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task SessionEndedForReplayAsync(
        Guid clinicId, string targetUserId, string? deviceLabel, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var where = string.IsNullOrWhiteSpace(deviceLabel) ? null : $" ({deviceLabel.Trim()})";

            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.SecondFactorReset,
                "Session interrompue",
                $"Une session{where} a été interrompue : un identifiant déjà remplacé a été présenté. "
                + "Vos autres appareils restent connectés. Si ce n'était pas vous, changez votre mot de passe.",
                DateTime.UtcNow,
                NotificationTargetKind.Security,
                actorUserId: null,
                targetUserId: targetUserId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    // Resolves the post-visit target: the appointment's DoctorId → its linked User; any miss → null = all staff.
    // The rule itself lives in StaffNotificationRules, because the push fan-out must target the same person.
    private Task<string?> ResolveTargetUserIdAsync(Guid clinicId, Guid? doctorId, CancellationToken cancellationToken) =>
        StaffNotificationRules.ResolveDoctorUserIdAsync(_doctors, clinicId, doctorId, cancellationToken);

    // Runs the write and broadcasts the realtime key only when the write reports that something became
    // visible in the feed (returns true) — a future-dated reminder or a no-op schedule returns false, so
    // clients aren't made to refetch for no visible change. Best-effort: never throws to the caller (the
    // core operation is already committed); logs at Error so a genuine fault stays visible.
    private async Task SafelyAsync(Guid clinicId, Func<Task<bool>> write, CancellationToken cancellationToken)
    {
        try
        {
            if (await write())
            {
                await _realtimeNotifier.NotifyEntityChangedAsync(clinicId, RealtimeResourceKey, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate staff notification(s) for clinic {ClinicId}", clinicId);
        }
    }

    private const string PostVisitReviewTitle = "Compte rendu de visite";

    private static string PostVisitReviewMessage(string patientName) =>
        $"La visite de {patientName} est terminée. Ajoutez son dossier médical.";

    private const string StockExpiringSoonTitle = "Péremption proche";

    // States the date AND the days remaining: the date is what the operator checks on the shelf, the count is
    // what tells them whether to act today. The count is derived here rather than passed in, so it can never
    // disagree with the date. Deliberately NOT part of the idempotency comparison — see below.
    private static string StockExpiringSoonMessage(string itemName, DateTime earliestExpiryUtc, DateTime nowUtc)
    {
        var days = (earliestExpiryUtc.Date - nowUtc.Date).Days;
        var remaining = days <= 0
            ? "aujourd'hui"
            : $"dans {days} jour{(days == 1 ? "" : "s")}";
        return $"Péremption proche : {itemName} — un lot expire le {FormatFrDate(earliestExpiryUtc)} ({remaining}).";
    }

    // The stable part of the message — item name + expiry date, everything except the countdown. Two messages
    // sharing this prefix are about the same batch, so the daily re-scan restates nothing and makes nobody
    // refetch. Comparing the WHOLE message would differ every single day (the countdown ticks down), which
    // would turn the "ensure" into a daily broadcast — the exact churn this alert is meant not to cause.
    private static string StockExpiringSoonKey(string itemName, DateTime earliestExpiryUtc) =>
        $"Péremption proche : {itemName} — un lot expire le {FormatFrDate(earliestExpiryUtc)} (";

    private const string BackupStaleTitle = "Sauvegarde à vérifier";

    /// <summary>
    /// Two wordings, because « jamais sauvegardé » and « la dernière remonte à trois jours » demand different
    /// things of the reader — and firing the alarming one on a clinic created this morning is how an alert gets
    /// dismissed permanently on day one.
    /// </summary>
    private static string BackupStaleMessage(DateTime? lastSuccessUtc, int staleAfterHours)
    {
        if (lastSuccessUtc is not DateTime last)
        {
            return "Aucune sauvegarde n'a encore été effectuée. Ouvrez « Paramètres » puis « Sauvegarde » "
                   + "pour en lancer une et vérifier le dossier de destination.";
        }

        // Whole hours, and derived here rather than passed in, so the count can never disagree with the date.
        var hours = Math.Max(0, (int)(DateTime.UtcNow - last).TotalHours);
        return $"{BackupStaleKey(last)} (il y a {hours} h, seuil : {staleAfterHours} h). "
               + "Vérifiez le dossier de destination dans « Paramètres ».";
    }

    /// <summary>
    /// The stable part of the message — everything up to the elapsed count. Two messages sharing this prefix are
    /// about the same last-successful-backup, so the daily re-evaluation restates nothing and makes nobody
    /// refetch.
    /// </summary>
    private static string BackupStaleKey(DateTime? lastSuccessUtc) =>
        lastSuccessUtc is DateTime last
            ? $"Dernière sauvegarde réussie le {FormatFr(last)}"
            : "Aucune sauvegarde n'a encore été effectuée.";

    /// <summary>
    /// The countdown is in the <b>title</b> so the four rows are distinguishable at a glance in the feed — with one
    /// shared title, a cabinet reading the bell could not tell « 3 jours » from the « 7 jours » it read last week.
    /// </summary>
    private static string SubscriptionWarningTitle(int thresholdDays) => thresholdDays switch
    {
        0 => "Abonnement — dernier jour",
        1 => "Abonnement — 1 jour restant",
        _ => $"Abonnement — {thresholdDays} jours restants"
    };

    /// <summary>
    /// Derived from the <b>threshold</b> and the end date, never from the live countdown: a message rebuilt from
    /// « days remaining » would differ every day, so the ensure would restate — and make everyone refetch — on every
    /// daily pass, which is the churn the dedupe exists to prevent.
    ///
    /// <para>It says what still works before what will stop, exactly as <see cref="SubscriptionRefusals"/> does: it
    /// is read chairside and the fear it answers first is « am I about to lose the patients' files? ».</para>
    /// </summary>
    private static string SubscriptionWarningMessage(DateTime endsOn) =>
        $"Votre abonnement se termine le {endsOn.ToString(SubscriptionRefusals.DateFormat, CultureInfo.InvariantCulture)}. "
        + "Vous pourrez toujours consulter et exporter vos données, mais plus enregistrer de nouveaux actes. "
        + "Rendez-vous dans « Abonnement » pour le renouveler.";

    private const string ReminderTitle = "Rappel de rendez-vous";

    private static string ReminderMessage(string patientName, DateTime appointmentDateTimeUtc) =>
        $"Rappel : rendez-vous pour {patientName} le {FormatFr(appointmentDateTimeUtc)}.";

    private static StaffNotification BuildReminder(
        Guid clinicId, Guid appointmentId, string patientName, DateTime appointmentDateTimeUtc, DateTime dueTimeUtc) =>
        new(
            Guid.NewGuid(), clinicId, NotificationCategory.Reminder,
            ReminderTitle,
            ReminderMessage(patientName, appointmentDateTimeUtc),
            dueTimeUtc, // effective feed time = due time; surfaces only once dueTime <= now
            NotificationTargetKind.Appointment,
            actorUserId: null,
            appointmentId: appointmentId);

    // An expiry is a calendar date, not a moment — no time-of-day, but still read in clinic-local time so a
    // batch expiring just after midnight UTC is not shown as the previous day.
    private static string FormatFrDate(DateTime utc) =>
        ClinicClock.ToClinicLocal(utc).ToString("dd/MM/yyyy", FrCulture);

    private static string FormatFr(DateTime utc) =>
        ClinicClock.ToClinicLocal(utc).ToString("dd/MM/yyyy 'à' HH:mm", FrCulture);

}
