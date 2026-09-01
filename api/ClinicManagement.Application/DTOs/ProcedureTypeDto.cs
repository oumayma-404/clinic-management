using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.DTOs;

public class ProcedureTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DefaultDurationMinutes { get; set; }
    public decimal? DefaultCost { get; set; }
    public string ColorHex { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>
    /// Clinical discipline (« Endodontie », « Prothèse fixe »); null = unfiled, which the UI groups last under
    /// « Sans catégorie ». Open text canonicalised on write — see <c>ProcedureTypeCategories</c>.
    /// </summary>
    public string? Category { get; set; }
    /// <summary>Resulting odontogram state (ToothCondition name) a dental act of this procedure produces; null = none.</summary>
    public string? ResultingCondition { get; set; }

    /// <summary>
    /// The charted diagnoses this act treats, each with its rank among that diagnosis' options (0 = first
    /// choice, least invasive). The inverse of <see cref="ResultingCondition"/>, and NOT derivable from it: no
    /// act ends in « Carie », so inverting that field leaves every pathology with nothing to offer.
    ///
    /// <para>Carried on the act rather than served as its own read, so the odontogram builds the
    /// diagnosis→acts index from the catalogue it already holds — and so <c>ConditionTreatments</c> stays the
    /// only copy of the clinical claim. Moving it to a per-act editable column later changes the source of this
    /// field and nothing else.</para>
    /// </summary>
    public List<ProcedureTreatsDto> Treats { get; set; } = new();
    public bool IsActive { get; set; }

    /// <summary>
    /// The act's material list (AC-P4.9/4.14) — the stock performing it consumes. Empty for an act that has
    /// opted out, which is the default and behaves exactly as before (AC-P4.11).
    /// </summary>
    public List<ProcedureTypeMaterialDto> Materials { get; set; } = new();

    /// <summary>Round-tripped by the edit form so a concurrent change is a 409 rather than a silent overwrite.</summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// One diagnosis an act treats. <c>Rank</c> orders the options for that diagnosis, least invasive first — and a
/// client pre-fills a plan line from rank 0 <b>only when exactly one act holds it</b>, because two acts at the
/// same rung (a simple and a surgical extraction) is a clinical judgement, not a tie to break.
/// </summary>
public class ProcedureTreatsDto
{
    public string Condition { get; set; } = string.Empty;
    public int Rank { get; set; }
}

/// <summary>One line of an act's material list: performing the act consumes N of this stock item.</summary>
public class ProcedureTypeMaterialDto
{
    public Guid StockItemId { get; set; }
    public int QuantityPerAct { get; set; }
}

/// <summary>
/// One hue family of the agenda-colour palette (<c>GET /api/procedure-types/colors</c>) — the unit the picker
/// offers, so it shows twelve swatches at rest rather than every colour the server accepts.
/// </summary>
public class ProcedureColorFamilyDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<ProcedureColorDto> Colors { get; set; } = new();
}

/// <summary>One selectable colour: the value that is stored, and what to call it.</summary>
public class ProcedureColorDto
{
    public string Hex { get; set; } = string.Empty;
    /// <summary>
    /// « Bleu moyen ». Composed server-side so the client needs no hex→name map of its own — it used to carry one,
    /// which meant a colour added here rendered with its raw hex as its label until somebody remembered to name it.
    /// </summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>« Clair » / « Moyen » / « Foncé » — the nuance strip's own caption, once a family is picked.</summary>
    public string Tone { get; set; } = string.Empty;
}

public static class ProcedureColorPaletteMapping
{
    public static List<ProcedureColorFamilyDto> ToDto(
        this IEnumerable<Domain.ValueObjects.ColorFamily> families) =>
        families
            .Select(family => new ProcedureColorFamilyDto
            {
                Key = family.Key,
                Label = family.LabelFr,
                Colors = family.Tones
                    .Select(tone => new ProcedureColorDto
                    {
                        Hex = tone.Hex,
                        Label = $"{family.LabelFr} {tone.ToneFr.ToLowerInvariant()}",
                        Tone = tone.ToneFr,
                    })
                    .ToList(),
            })
            .ToList();
}

public static class ProcedureTypeMappingExtensions
{
    /// <summary>
    /// The single mapping for a procedure type. It exists because the DTO was hand-built at four call sites
    /// (create, update, get-one, get-many): adding <see cref="ProcedureTypeDto.Materials"/> to three of the
    /// four would have left the list quietly empty on the fourth, and an empty material list is exactly how
    /// an act that has opted out reads (AC-P4.11) — the one failure mode it must never be confusable with.
    /// </summary>
    public static ProcedureTypeDto ToDto(this Domain.Entities.ProcedureType procedureType) =>
        new()
        {
            Id = procedureType.Id,
            Name = procedureType.Name,
            DefaultDurationMinutes = procedureType.DefaultDurationMinutes,
            DefaultCost = procedureType.DefaultCost,
            ColorHex = procedureType.Color.Value,
            Description = procedureType.Description,
            Category = procedureType.Category,
            ResultingCondition = procedureType.ResultingCondition?.ToString(),
            Treats = ConditionTreatments
                .RanksFor(procedureType.ResultingCondition, procedureType.Category)
                .Select(t => new ProcedureTreatsDto { Condition = t.Condition.ToString(), Rank = t.Rank })
                .ToList(),
            IsActive = procedureType.IsActive,
            Materials = procedureType.Materials
                .Select(m => new ProcedureTypeMaterialDto
                {
                    StockItemId = m.StockItemId,
                    QuantityPerAct = m.QuantityPerAct,
                })
                .ToList(),
            Version = procedureType.Version,
            CreatedAt = procedureType.CreatedAt,
            UpdatedAt = procedureType.UpdatedAt,
        };
}
