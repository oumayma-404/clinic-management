namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One Google→App import pass, as « Imports Google » and the « Annuler cet import » banner read it.
/// </summary>
public class CalendarImportRunDto
{
    public Guid Id { get; set; }
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// Who set it off, already in French: a person's name or « Import automatique ». The screen never sees the
    /// raw <c>job|…</c> actor — that is a ledger convention, not a sentence.
    /// </summary>
    public string TriggeredBy { get; set; } = string.Empty;

    public int AppointmentsCreated { get; set; }
    public int PatientsCreated { get; set; }
    public int AppointmentsUpdated { get; set; }

    public DateTime? RevertedAtUtc { get; set; }

    /// <summary>
    /// How many of the rows it created are still there. <b>Zero is why a run stops being offered for undo</b> —
    /// a practice that has already deleted them by hand has nothing left to undo, and a button that would remove
    /// nothing is worse than no button.
    /// </summary>
    public int RowsRemaining { get; set; }

    /// <summary>True when there is something to undo and nobody has undone it.</summary>
    public bool CanRevert => RevertedAtUtc is null && RowsRemaining > 0;
}

/// <summary>
/// One row an undo will <b>not</b> delete, and why — in French, ready to print.
///
/// <para>Named rather than counted: a revert that silently keeps four rows leaves a practice looking at a list it
/// was told would be empty, with nothing to explain the difference.</para>
/// </summary>
public class CalendarImportKeptRowDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime? When { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// What « Annuler cet import » would do, before it does it — the dry run that <b>is</b> the safety, since the
/// person pressing the button is the cabinet rather than the vendor.
/// </summary>
public class CalendarImportRevertPreviewDto
{
    public Guid RunId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public bool AlreadyReverted { get; set; }

    public int AppointmentsToDelete { get; set; }
    public int PatientsToDelete { get; set; }

    /// <summary>Every row that will survive, each naming its own reason.</summary>
    public List<CalendarImportKeptRowDto> Kept { get; set; } = new();
}

/// <summary>What the undo actually did.</summary>
public class CalendarImportRevertResultDto
{
    public Guid RunId { get; set; }
    public int AppointmentsDeleted { get; set; }
    public int PatientsDeleted { get; set; }
    public List<CalendarImportKeptRowDto> Kept { get; set; } = new();
}
