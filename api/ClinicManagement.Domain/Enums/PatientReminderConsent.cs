namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Whether this patient has agreed to be contacted by SMS/WhatsApp for appointment reminders and recalls.
///
/// <para><b>Why this is tri-state and not a bool.</b> « Refused » and « never asked » are different facts and a
/// bool cannot hold both. The practice needs to see which patients still owe an answer — a single
/// <c>false</c> would make an unasked patient indistinguishable from one who said no, and the cabinet would
/// have no list to work through.</para>
///
/// <para>⚠️ <b><see cref="NotRecorded"/> still receives reminders, and that is a deliberate, dated decision.</b>
/// Every patient already on file was recorded before this column existed; treating them as refusals would
/// silently stop every reminder in every cabinet on the day this ships — a practice would discover it through
/// empty waiting rooms, not through a message. The phone number was given *for* being contacted about care, and
/// an appointment reminder is care communication rather than marketing. What this enum adds is the ability to
/// say <see cref="Refused"/> and have it obeyed, which is what did not exist. Asking every existing patient is
/// a real obligation, but it belongs to the practice's own workflow, not to a migration that mutes them.</para>
/// </summary>
public enum PatientReminderConsent
{
    /// <summary>Nobody has asked yet. Reminders are sent — see the type-level note for why.</summary>
    NotRecorded = 0,

    /// <summary>The patient agreed, and <c>ReminderConsentRecordedAtUtc</c> says when.</summary>
    Granted = 1,

    /// <summary>
    /// The patient said no. <b>No reminder and no recall is ever enqueued</b>, whatever the clinic's channels
    /// say and whatever number is on file.
    /// </summary>
    Refused = 2,
}
