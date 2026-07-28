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
    Failed = 4
}
