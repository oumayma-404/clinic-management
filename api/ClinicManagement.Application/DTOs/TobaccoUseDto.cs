namespace ClinicManagement.Application.DTOs;

/// <summary>
/// « Tabac » on the wire. Absent/null means nobody has asked — an answered « Non-fumeur » is a present block with
/// <c>Status = "NonSmoker"</c>, and the two are different clinical facts.
/// </summary>
/// <remarks>
/// <see cref="Status"/> and <see cref="Unit"/> carry the enum member's own <b>name</b>, not a French label:
/// storage keys are English and the display map lives on the client, so rewording « Ancien fumeur » cannot change
/// what is stored or how a read branches.
/// </remarks>
public class TobaccoUseDto
{
    /// <summary>One of <c>NonSmoker</c> · <c>Smoker</c> · <c>FormerSmoker</c>.</summary>
    public string? Status { get; set; }

    /// <summary>How many per day. Only ever set alongside <c>Smoker</c> — the value object enforces it.</summary>
    public int? PerDay { get; set; }

    /// <summary>One of <c>Cigarettes</c> · <c>Packs</c>. Null whenever <see cref="PerDay"/> is.</summary>
    public string? Unit { get; set; }
}
