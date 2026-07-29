namespace ClinicManagement.Application.DTOs;

public class DentalRecordDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    /// <summary>Derived summary of the acts' procedure names (read-only).</summary>
    public string ProcedureType { get; set; } = string.Empty;
    /// <summary>Derived total = sum of act costs (read-only).</summary>
    public decimal Cost { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; } // derived: Cost − AmountPaid
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
    public bool IsAdultTeeth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public List<DentalRecordActDto> Acts { get; set; } = new();
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// What happened to the money when this fiche was saved. Present on the create/update responses only — it is
    /// the outcome of a post-commit side effect, not stored state, so a later <c>GET</c> leaves it null.
    /// <para>
    /// It exists because the billing is best-effort for the <i>record</i> but must never be silent about the
    /// <i>cash</i>: a swallowed failure would put the user right back where they started, believing money was
    /// recorded when it was not.
    /// </para>
    /// </summary>
    public DentalRecordBillingDto? Billing { get; set; }
}

/// <summary>What saving a fiche did about its « Montant payé ».</summary>
public enum DentalRecordBillingOutcome
{
    /// <summary>No payment on the fiche, so nothing was billed. Not an error.</summary>
    NotCollected = 0,

    /// <summary>A note d'honoraires was issued and the payment recorded.</summary>
    Billed = 1,

    /// <summary>The fiche was already on a live note — the expected outcome of re-saving one.</summary>
    AlreadyBilled = 2,

    /// <summary>The record saved, the billing did not. The user has to be told.</summary>
    Failed = 3
}

/// <summary>The money outcome of a fiche save (see <see cref="DentalRecordDto.Billing"/>).</summary>
public class DentalRecordBillingDto
{
    /// <summary>A <see cref="DentalRecordBillingOutcome"/> name.</summary>
    public string Outcome { get; set; } = string.Empty;

    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? AmountCollected { get; set; }

    /// <summary>The French reason, for <c>Failed</c> and <c>AlreadyBilled</c>.</summary>
    public string? Message { get; set; }
}

/// <summary>One act on a dental record (procedure + teeth + cost + resulting odontogram state).</summary>
public class DentalRecordActDto
{
    public Guid Id { get; set; }
    public Guid? ProcedureTypeId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    /// <summary>The act's total fee (authoritative).</summary>
    public decimal Cost { get; set; }
    /// <summary>Per-unit price <see cref="Cost"/> was built from; null when never captured (legacy rows).</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>True when <see cref="Cost"/> is <see cref="UnitCost"/> × teeth; false = flat fee.</summary>
    public bool IsPerTooth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public string? ResultingCondition { get; set; }
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}

/// <summary>One requested act when creating/updating a dental record.</summary>
public class DentalActInput
{
    public Guid? ProcedureTypeId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    /// <summary>The act's total fee. The server stores it as sent — it is never recomputed from the unit price.</summary>
    public decimal Cost { get; set; }
    /// <summary>Optional per-unit price the total was built from (pricing provenance for the editor + invoice).</summary>
    public decimal? UnitCost { get; set; }
    /// <summary>Whether <see cref="Cost"/> is per treated tooth (else a flat session fee). Ignored when no teeth.</summary>
    public bool IsPerTooth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    /// <summary>Resulting odontogram state (ToothCondition name); null/empty/"Sain" = no odontogram entry.</summary>
    public string? ResultingCondition { get; set; }
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}
