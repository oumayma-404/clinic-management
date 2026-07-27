namespace ClinicManagement.Application.DTOs;

/// <summary>
/// Whether a patient can be deleted, and if not, exactly what is attached — so the confirm dialog can say it
/// when it opens instead of making the user click Supprimer to find out.
/// </summary>
public class PatientDeletionCheckDto
{
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;

    /// <summary>True only when nothing at all is attached.</summary>
    public bool CanDelete { get; set; }

    /// <summary>True when the patient is already archived (the alternative offered when deletion is refused).</summary>
    public bool IsArchived { get; set; }

    /// <summary>Whether archiving is available instead — false when a balance is due or a visit is booked.</summary>
    public bool CanArchive { get; set; }

    /// <summary>Why archiving is unavailable, when it is. French, ready to display.</summary>
    public string? ArchiveBlockedReason { get; set; }

    public List<PatientDeletionBlockerDto> Blockers { get; set; } = new();
}

public class PatientDeletionBlockerDto
{
    /// <summary>Stable machine key (e.g. <c>invoices</c>) — the frontend keys off this, never the label.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>French, already pluralised for <see cref="Count"/> (e.g. « factures »).</summary>
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    /// <summary>Patient-detail tab this record kind lives on, so the dialog can link to it. Null when there is none.</summary>
    public string? Tab { get; set; }
}
