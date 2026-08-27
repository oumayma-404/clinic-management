using ClinicManagement.Application.Features.Recall;

namespace ClinicManagement.Application.DTOs;

/// <summary>One reason a patient is on the « à rappeler » worklist.</summary>
public class RecallReasonDto
{
    /// <summary>English enum name — the wire form; the French label is mapped at display time.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>When this reason became actionable (échéance due date, devis acceptance, recall due date).</summary>
    public DateTime DueSince { get; set; }

    public int DaysOverdue { get; set; }

    /// <summary>Factual context only — a devis number, an amount. Never a sentence.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// One patient worth calling, with <b>every</b> reason to call them.
///
/// <para>The list used to answer only "not seen for the recall interval". It now aggregates four reasons — an overdue
/// échéance, a stalled devis, an unanswered devis, and the original overdue visit — because for a perio/implant
/// practice the time-since-last-visit rule is the least informative of them: a patient seen last week who stopped
/// halfway through an accepted plan is both lost revenue and an unfinished surgical case, and no time-since-visit
/// rule can surface them.</para>
///
/// <para><b>One row per patient, not per reason.</b> Snooze state lives on the patient
/// (<c>Patient.RecallSnoozedUntil</c>), so a per-reason row would let « Reporter » on one reason silently hide
/// another — and staff make one call covering everything anyway.</para>
/// </summary>
public class RecallDto
{
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? LastVisitDate { get; set; }

    /// <summary>The headline (most urgent) reason's date.</summary>
    public DateTime DueDate { get; set; }

    /// <summary>Days overdue on the headline reason. Drives the list's ordering.</summary>
    public int DaysOverdue { get; set; }

    /// <summary>The most urgent reason's kind, for the row's primary badge.</summary>
    public string PrimaryReason { get; set; } = nameof(RecallReasonKind.OverdueVisit);

    /// <summary>Every reason, most urgent first. Never empty — a patient with none is not on the list.</summary>
    public List<RecallReasonDto> Reasons { get; set; } = new();

    /// <summary>
    /// Free-text note staff attached when snoozing or marking contacted (<c>Patient.RecallReason</c>). Renamed from
    /// <c>Reason</c>, which collided with the new machine-derived reasons and read as though it explained them.
    /// </summary>
    public string? Note { get; set; }

    public DateTime? LastContactedAt { get; set; }
}

/// <summary>The per-clinic recall configuration (currently just the interval in months, default 6).</summary>
public class RecallSettingsDto
{
    public int IntervalMonths { get; set; }
}
