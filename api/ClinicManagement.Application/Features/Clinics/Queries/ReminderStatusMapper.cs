using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>
/// One outbox row → one delivery-log row.
///
/// <para>Extracted from <see cref="GetClinicReminderStatusQuery"/> when the paged
/// <see cref="GetClinicReminderLogQuery"/> arrived, rather than copied into it. Two mappings of the same row is
/// the § 5.10 defect — the two would drift the first time a field was added, and the difference would show as the
/// settings widget and the page disagreeing about the same message.</para>
/// </summary>
public static class ReminderStatusMapper
{
    public static ReminderStatusDto ToDto(Notification n) => new()
    {
        Id = n.Id,
        Channel = n.Type.ToString(),
        RecipientMasked = MaskRecipient(n.Patient?.PhoneNumber?.Value),
        // AC-P3.9 — the name, so a failed row names someone; the phone stays masked (AC-P3.10).
        PatientName = n.Patient == null ? null : n.Patient.GetFullName(),
        AppointmentAt = n.Appointment?.AppointmentDateTime,
        IsRecall = n.AppointmentId == null,
        Status = n.Status switch
        {
            NotificationStatus.Sent => ReminderDeliveryStatus.Sent,
            NotificationStatus.Failed => ReminderDeliveryStatus.Failed,
            NotificationStatus.Blocked => ReminderDeliveryStatus.Blocked,
            _ => ReminderDeliveryStatus.Pending,
        },
        FailureReason = string.IsNullOrWhiteSpace(n.ErrorMessage) ? null : n.ErrorMessage,
        // AC-4.9 — the enum member's own name, so the screen never has to read the French sentence beside it to know
        // what kind of hold this is. Null unless the row is actually blocked: `MarkAsBlocked`/`Unblock` write and clear
        // the reason together, but a released row that kept a stale value would read as held on a screen that branches
        // on this field.
        BlockReason = n.Status == NotificationStatus.Blocked ? n.BlockedReason?.ToString() : null,
        ScheduledAt = n.ScheduledFor,
        SentAt = n.SentAt,
    };

    /// <summary>
    /// Display-only PII mask — the last two digits, e.g. « ••••56 ». Distinct from Infrastructure's
    /// <c>ReminderPhone.Mask</c>, which masks for logs; this one is what a user reads on screen.
    /// </summary>
    private static string MaskRecipient(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "(aucun numéro)";
        }

        var trimmed = phone.Trim();
        return trimmed.Length <= 2 ? "••" : "••••" + trimmed[^2..];
    }
}
