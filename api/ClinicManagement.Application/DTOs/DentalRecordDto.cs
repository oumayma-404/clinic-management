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
