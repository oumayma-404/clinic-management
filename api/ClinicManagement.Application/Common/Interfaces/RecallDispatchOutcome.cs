namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// What actually happened when a « relance » was handed to the reminder outbox (AC-P3.1). The recall command
/// used to receive nothing at all: <c>ScheduleRecallAsync</c> returned early when no channel was configured,
/// the handler stamped « contacté » and snoozed the patient 30 days regardless, and the UI toasted
/// « Rappel envoyé à … ». The caller now has to branch on this, so a no-op can no longer read as a send.
/// </summary>
public enum RecallDispatchOutcome
{
    /// <summary>At least one outbox row was created. The dispatcher will attempt it on its next tick.</summary>
    Enqueued = 1,

    /// <summary>
    /// No SMS/WhatsApp channel is enabled for this clinic, so nothing was — or ever could be — queued.
    /// The command must refuse rather than snooze the patient for a month.
    /// </summary>
    NoChannelConfigured = 2,

    /// <summary>The patient has no deliverable phone number, so no channel can reach them.</summary>
    NoDeliverablePhone = 3,

    /// <summary>
    /// The enqueue itself faulted (DB/settings failure, already logged). Distinct from the two "nothing to
    /// do" outcomes: the operator should retry rather than go and configure a channel.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// The cabinet's WhatsApp reminder forfait is spent for this Tunisian month, and WhatsApp is the <b>only</b>
    /// channel it can send on — so nothing was queued (<c>vendor-whatsapp-messaging-quota</c> AC-5.1).
    ///
    /// <para><b>⚠️ Its own outcome, distinct from <see cref="NoChannelConfigured"/>, and that distinction is AC-5.4.</b>
    /// Today's vocabulary would answer a WhatsApp-only cabinet with the no-channel refusal, whose sentence tells the
    /// practice to go and configure a channel it has already configured — advice it cannot act on. This one names the
    /// forfait and offers « Marquer comme contacté », and like every other non-success branch it leaves the patient
    /// exactly as they were (AC-5.2).</para>
    ///
    /// <para>⚠️ It is reached <b>only</b> when WhatsApp is the sole sendable channel. With SMS also sendable the relance
    /// is <see cref="Enqueued"/> and <b>not</b> refused (AC-5.3): a channel being exhausted is not the same as having
    /// no channel, and the WhatsApp row is simply held at dispatch.</para>
    /// </summary>
    MessagingAllowanceExhausted = 5,

    /// <summary>
    /// The patient has refused automated reminders, so nothing was queued and nothing ever will be while that
    /// stands.
    ///
    /// <para>⚠️ <b>Its own outcome rather than reusing <see cref="NoDeliverablePhone"/></b>, for the reason
    /// <see cref="MessagingAllowanceExhausted"/> is not <see cref="NoChannelConfigured"/>: the phone sentence
    /// tells reception to go and fix a number, and fixing the number is precisely what must NOT make the
    /// message go out. A refusal is answered by calling the patient, not by editing their record.</para>
    /// </summary>
    ReminderConsentRefused = 6
}
