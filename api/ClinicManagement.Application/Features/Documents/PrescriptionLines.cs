using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// The content arrays of the two ordonnances a séance emits — <c>content.medications</c> on the médicament
/// ordonnance and <c>content.examens</c> on the demande d'examens — read and written from the Application
/// layer, the seam the fiche de soins needs and <c>PrescriptionContent</c> / <c>ExamenContent</c>
/// (Infrastructure) cannot provide, since Application must not reference Infrastructure.
///
/// <para>
/// ⚠️ <b>Two arrays because they are two documents, and a médicament and an examen may not share a sheet.</b>
/// See <c>DocumentTypes.Examens</c> for why. The shapes are deliberately not symmetric: a médicament line has
/// nine properties the norms care about, an examen line has one.
/// </para>
///
/// <para>
/// ⚠️ <b>This is NOT a fourth copy of the printed-line format, and it must never become one.</b>
/// <c>PrescriptionContent</c> is the one authority for what a line looks like on paper — the
/// posologie, the voie, the quantité, the durée, the DCI, in the order R.5132-3 wants them — and this file
/// deliberately does not know any of that. What it produces is a <see cref="ShortLabel"/>: five or six words
/// for a table row, « Augmentin Comprimé 1 g », where the printed line runs to a full sentence. Two different
/// renderings of the same data, for two different surfaces; unifying them would either bloat the history row
/// or truncate the ordonnance.
/// </para>
///
/// <para>
/// ⚠️ <b>The JSON is camelCase and that is a hard requirement, not a style.</b> The document editor reads
/// these lines back with plain property access (<c>med.timesPerDay</c>), which is case-sensitive — so a
/// PascalCase write would hand a dentist an ordonnance whose posologies had silently emptied. (The C# reader
/// is case-insensitive and would not have noticed.)
/// </para>
/// </summary>
public static class PrescriptionLines
{
    /// <summary>The <c>ContentJson</c> keys the fiche writes. Same keys the document editor writes.</summary>
    public const string MedicationsKey = "medications";
    public const string RenewalsKey = "renewals";
    public const string DateKey = "date";

    /// <summary>
    /// The examens array. The literal is repeated in <c>ExamenContent.ExamensKey</c> because that renderer
    /// lives in Infrastructure and this layer may not reference it — the same split <see cref="MedicationsKey"/>
    /// already lives with.
    /// </summary>
    public const string ExamensKey = "examens";

    /// <summary>
    /// ⚠️ <b>No custom <c>Encoder</c>, and that is deliberate rather than an omission.</b> The obvious change
    /// here is <c>UnsafeRelaxedJsonEscaping</c>, so « Ibuprofène » persists as itself instead of
    /// <c>Ibuprofène</c> — and it would achieve nothing, because every string this method returns is
    /// immediately re-serialised by <see cref="PractitionerRenderSnapshot.ApplyTo"/>, which parses the object
    /// to add the cachet and the n° d'ordre and emits it with the default encoder. That has been true of every
    /// document the API has ever written, editor-authored ones included, so escaped non-ASCII **is** the stored
    /// corpus; setting it here would leave a comment claiming a byte shape the next call undoes.
    /// <para>
    /// It costs nothing either way: every reader parses the JSON (`JSON.parse` in the editor,
    /// <c>System.Text.Json</c> in the renderer), and both decode the escape. A test that asserts on the raw
    /// string rather than on the parsed value is the only thing this can break, which is what it did.
    /// </para>
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// The <c>ContentJson</c> for a séance's <b>médicament</b> ordonnance. The practitioner/cabinet reserved
    /// keys are NOT written here — <c>PractitionerRenderSnapshot.ApplyTo</c> owns those, and layering them on
    /// afterwards is what guarantees a caller cannot inject one.
    ///
    /// <para>
    /// ⚠️ Expects médicament lines. It does not filter, because the split belongs to one place
    /// (<c>FicheOrdonnanceEmitter</c>) and a second filter here would be a second answer to « which document
    /// does this line belong on? ». An examen line reaching it would be written with its <c>kind</c> intact
    /// and printed as a drug line, which is the defect this whole split exists to remove.
    /// </para>
    /// </summary>
    public static string BuildPrescriptionContentJson(PrescriptionInput prescription, DateTime interventionDate)
    {
        ArgumentNullException.ThrowIfNull(prescription);

        var lines = new JsonArray();
        foreach (var line in prescription.Lines)
        {
            lines.Add(JsonSerializer.SerializeToNode(Wire.From(line), WriteOptions));
        }

        var content = new JsonObject
        {
            [DateKey] = interventionDate.ToString("yyyy-MM-dd"),
            [MedicationsKey] = lines,
        };

        var renewals = prescription.Renewals?.Trim();
        if (!string.IsNullOrEmpty(renewals))
        {
            content[RenewalsKey] = renewals;
        }

        return content.ToJsonString();
    }

    /// <summary>
    /// The <c>ContentJson</c> for a séance's <b>demande d'examens</b>: the date and an <c>examens</c> array of
    /// <c>{ name }</c>, and nothing else.
    ///
    /// <para>
    /// ⚠️ <b>No <c>renewals</c>, and that is not an omission.</b> Renewal is a dispensing concept; an examen
    /// prescription is single-use by default. Writing the key here would print « Ordonnance à renouveler 2
    /// fois » on a panoramique, and the renouvellement the dentist typed belongs to the médicament sheet.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>No <c>kind</c> on the lines either.</b> Every line of this document is an examen by construction —
    /// the document type says so — and a per-line discriminator inside a single-kind array is exactly the
    /// half-measure the médicament ordonnance was carrying before the split.
    /// </para>
    /// </summary>
    public static string BuildExamensContentJson(
        IReadOnlyList<PrescriptionLineInput> examenLines,
        DateTime interventionDate)
    {
        ArgumentNullException.ThrowIfNull(examenLines);

        var lines = new JsonArray();
        foreach (var line in examenLines)
        {
            var name = line.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            lines.Add(new JsonObject { ["name"] = name });
        }

        var content = new JsonObject
        {
            [DateKey] = interventionDate.ToString("yyyy-MM-dd"),
            [ExamensKey] = lines,
        };

        return content.ToJsonString();
    }

    /// <summary>
    /// The lines already stored on a document, as the fiche's editor needs them back. An empty list for a blank
    /// or malformed <c>ContentJson</c> — never an exception, because a prescription that cannot be reopened is
    /// worse than one that reopens with a line the dentist has to retype.
    /// </summary>
    public static IReadOnlyList<PrescriptionLineInput> Read(string? contentJson)
    {
        var node = ParseObject(contentJson);
        if (node is null)
        {
            return Array.Empty<PrescriptionLineInput>();
        }

        return ReadWire(node).Select(w => w.ToInput()).ToList();
    }

    /// <summary>The renouvellement mention stored on a document, or null.</summary>
    public static string? ReadRenewals(string? contentJson)
    {
        var node = ParseObject(contentJson);
        var value = node?[RenewalsKey];
        var text = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// The examens already stored on a demande d'examens, as the fiche's editor needs them back — every one
    /// carrying <c>Kind = examen</c>, since the document type is what makes them examens.
    /// </summary>
    public static IReadOnlyList<PrescriptionLineInput> ReadExamens(string? contentJson)
    {
        var node = ParseObject(contentJson);
        if (node is null || node[ExamensKey] is not JsonArray array)
        {
            return Array.Empty<PrescriptionLineInput>();
        }

        var lines = new List<PrescriptionLineInput>();
        foreach (var entry in array)
        {
            var name = entry?["name"];
            var text = name is JsonValue v && v.TryGetValue<string>(out var s) ? s : name?.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            lines.Add(new PrescriptionLineInput
            {
                Kind = PrescriptionLineKinds.Examen,
                Name = text.Trim(),
            });
        }

        return lines;
    }

    /// <summary>Row-sized labels for a demande d'examens. An examen is already a phrase, so it is only clamped.</summary>
    public static IReadOnlyList<string> ExamenShortLabels(string? contentJson) =>
        ReadExamens(contentJson)
            .Select(l => ShortLabel(PrescriptionLineKinds.Examen, l.Name, null))
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

    /// <summary>
    /// Five-or-six-word labels for a séance history row — « Augmentin Comprimé 1 g », « Radiographie
    /// panoramique dentaire ». See the type remark for why these are not the printed lines.
    /// </summary>
    public static IReadOnlyList<string> ShortLabels(string? contentJson)
    {
        var node = ParseObject(contentJson);
        if (node is null)
        {
            return Array.Empty<string>();
        }

        // A pre-array ordonnance holds `medications` as one plain string. It is a prescription and it must
        // still show on the row, so it becomes a single label rather than nothing.
        if (node[MedicationsKey] is JsonValue legacy && legacy.TryGetValue<string>(out var text))
        {
            var trimmed = text.Trim();
            return string.IsNullOrEmpty(trimmed) ? Array.Empty<string>() : new[] { Clamp(trimmed) };
        }

        return ReadWire(node)
            .Select(w => ShortLabel(w.Kind, w.Name, w.Dosage))
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
    }

    /// <summary>
    /// One row-sized label. A médicament carries its dosage because « Augmentin » alone does not say what was
    /// prescribed; an examen is already a phrase and takes nothing.
    /// </summary>
    public static string ShortLabel(string? kind, string? name, string? dosage)
    {
        var label = name?.Trim() ?? string.Empty;
        if (label.Length == 0)
        {
            return string.Empty;
        }

        if (!PrescriptionLineKinds.IsExamen(kind))
        {
            var strength = dosage?.Trim();
            if (!string.IsNullOrEmpty(strength))
            {
                label = $"{label} {strength}";
            }
        }

        return Clamp(label);
    }

    /// <summary>
    /// A free-text examen can be a paragraph. The row shows its opening; the whole thing stays one tap away on
    /// the ordonnance itself, which is where a clinical instruction should be read anyway.
    /// </summary>
    private const int MaxLabelLength = 60;

    private static string Clamp(string label) =>
        label.Length <= MaxLabelLength ? label : label[..(MaxLabelLength - 1)].TrimEnd() + "…";

    private static JsonObject? ParseObject(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(contentJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<Wire> ReadWire(JsonObject content)
    {
        if (content[MedicationsKey] is not JsonArray array)
        {
            return Array.Empty<Wire>();
        }

        try
        {
            return array.Deserialize<List<Wire>>(ReadOptions) ?? new List<Wire>();
        }
        catch (JsonException)
        {
            return Array.Empty<Wire>();
        }
    }

    /// <summary>
    /// The on-the-wire shape of one line. Named after the JSON rather than the domain because that is what it
    /// is: the contract shared with <c>document-editor-content.tsx</c>'s <c>PrescriptionLine</c> and read by
    /// <c>PrescriptionContent.MedicationEntry</c>.
    /// </summary>
    private sealed class Wire
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public string? Dosage { get; set; }
        public string? Dose { get; set; }
        public string? TimesPerDay { get; set; }
        public string? Route { get; set; }
        public string? Quantity { get; set; }
        public string? Duration { get; set; }
        public string? DurationUnit { get; set; }
        public string? MedicationId { get; set; }
        public List<string>? Dci { get; set; }

        public static Wire From(PrescriptionLineInput line)
        {
            var kind = PrescriptionLineKinds.Normalize(line.Kind);
            var isExamen = kind == PrescriptionLineKinds.Examen;

            return new Wire
            {
                Kind = kind,
                // The four the editor always writes, blank included — matching it exactly is what lets the two
                // surfaces edit the same document without either seeing a missing property.
                Name = line.Name?.Trim() ?? string.Empty,
                Dosage = isExamen ? string.Empty : line.Dosage?.Trim() ?? string.Empty,
                TimesPerDay = isExamen ? string.Empty : line.TimesPerDay?.Trim() ?? string.Empty,
                Duration = isExamen ? string.Empty : line.Duration?.Trim() ?? string.Empty,
                // The optional ones are omitted when absent (WhenWritingNull), as the editor omits them.
                Dose = isExamen ? null : Blank(line.Dose),
                // Written only when it is « mois »: an absent unit reads as jours everywhere, so writing the
                // default would put a key on every line for no change on the paper.
                DurationUnit = isExamen || DurationUnits.Normalize(line.DurationUnit) != DurationUnits.Months
                    ? null
                    : DurationUnits.Months,
                Route = isExamen ? null : Blank(line.Route),
                Quantity = isExamen ? null : Blank(line.Quantity),
                MedicationId = isExamen || !line.MedicationId.HasValue
                    ? null
                    : line.MedicationId.Value.ToString(),
                Dci = isExamen || line.Dci.Count == 0
                    ? null
                    : line.Dci.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToList(),
            };
        }

        public PrescriptionLineInput ToInput() => new()
        {
            Kind = PrescriptionLineKinds.Normalize(Kind),
            Name = Name,
            Dosage = Dosage,
            Dose = Dose,
            TimesPerDay = TimesPerDay,
            Route = Route,
            Quantity = Quantity,
            Duration = Duration,
            DurationUnit = DurationUnit,
            MedicationId = Guid.TryParse(MedicationId, out var id) ? id : null,
            Dci = Dci?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToList() ?? new List<string>(),
        };

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
