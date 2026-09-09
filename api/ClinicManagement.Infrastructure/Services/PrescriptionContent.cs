using System.Text.Json;
using ClinicManagement.Application.Features.Documents;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// One prescribed line as printed: the médicament, the posologie indented under it, and whatever else the
/// prescriber filled in (la voie, la quantité) on a third line.
/// <para>
/// ⚠️ <b>Only <see cref="Heading"/> and <see cref="Posology"/> are underlined</b> — those two are what a
/// pharmacist reads off the sheet, and underlining everything underlines nothing. <see cref="Details"/> is
/// plain, which is also why it is a third part rather than a tail on the posologie.
/// </para>
/// </summary>
public sealed record PrescriptionLine(string Heading, string Posology, string Details);

/// <summary>The composed ordonnance body: the prescribed lines plus the optional renewal mention.</summary>
public sealed record PrescriptionBody(IReadOnlyList<PrescriptionLine> Lines, string? RenewalMention);

/// <summary>
/// Builds the body of an ordonnance from its <c>ContentJson</c>. Pure and deterministic so it can be
/// unit-tested without rendering a PDF — the sibling of <see cref="LiaisonContent"/> and
/// <see cref="CertificatTextBuilder"/>.
/// <para>
/// ⚠️ <b>A line is THREE parts, not one sentence, and the split is what the paper is.</b> The pharmacist reads
/// « Augmentin (1 g) » on its own line and the posologie indented under it — « 1 comprimé * 3 / jour pendant
/// 12 jours » — under a « 1/ » number, with a rule closing the list so nothing can be added below it. Those two
/// are underlined and the third (la voie, la quantité) is not. A single run-on sentence is what this replaced,
/// and re-flattening the parts undoes the whole shape.
/// </para>
/// <para>
/// Each line still carries what R.5132-3 requires of the paper: the médicament, la dose, la posologie, la voie,
/// la durée and la quantité. The <b>renouvellement</b> mention is per-ordonnance, not per line: it governs the
/// document, and printing it against one médicament would read as applying to that one only.
/// </para>
/// Every element is optional and omitted when unset, so a legacy prescription still prints its médicament and
/// whatever posologie it carried.
/// </summary>
public static class PrescriptionContent
{
    /// <summary>Printed when the prescriber marked the ordonnance non-renewable.</summary>
    public const string NonRenewableMention = "Ordonnance non renouvelable.";

    public static PrescriptionBody Build(IReadOnlyDictionary<string, string> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = ParseLines(content.GetValueOrDefault("medications"));
        return new PrescriptionBody(lines, RenewalMention(content.GetValueOrDefault("renewals")));
    }

    /// <summary>
    /// The médicament's own line — « Augmentin (1 g) ». The dosage is parenthesised rather than appended so
    /// the strength cannot be misread as part of the name.
    /// </summary>
    public static string FormatHeading(string? name, string? dosage)
    {
        var text = Trimmed(name) ?? "Médicament";
        var strength = Trimmed(dosage);
        return strength == null ? text : $"{text} ({strength})";
    }

    /// <summary>
    /// The posologie printed under the médicament — « 1 comprimé * 3 / jour pendant 12 jours ». Returns an
    /// empty string when the line carries none, which is a first-class case: an examen line, and any médicament
    /// a prescriber left unposologised.
    /// <para>
    /// ⚠️ <b>The DCI is not printed any more.</b> It is still snapshotted on the line — deactivating a
    /// catalogue entry must not rewrite an issued ordonnance, and the fiche's own row label reads it — it is
    /// simply not on the paper: « (DCI : Amoxicilline) » after the brand name a dentist chose duplicates it for
    /// every catalogue médicament, on the one part of the sheet that has to be read at a glance.
    /// </para>
    /// </summary>
    public static string FormatPosology(
        string? dose,
        string? timesPerDay,
        string? duration,
        string? durationUnit)
    {
        var text = Trimmed(dose) ?? string.Empty;

        var frequency = Trimmed(timesPerDay);
        if (frequency != null)
        {
            text = text.Length == 0 ? $"{frequency} / jour" : $"{text} * {frequency} / jour";
        }

        var durationText = Trimmed(duration);
        if (durationText != null)
        {
            var unit = DurationUnits.Label(durationUnit, durationText);
            text += text.Length == 0 ? $"Pendant {durationText} {unit}" : $" pendant {durationText} {unit}";
        }

        return text;
    }

    /// <summary>
    /// What the prescriber added beyond the posologie — la voie d'administration (« par voie orale », printed
    /// as entered, since the norms name no closed list) and la quantité à délivrer, which is what makes the
    /// line dispensable. Printed plain under the posologie: both are R.5132-3 mentions and neither is what the
    /// underline is for.
    /// </summary>
    public static string FormatDetails(string? route, string? quantity)
    {
        var text = Trimmed(route) ?? string.Empty;
        var quantityText = Trimmed(quantity);
        return quantityText == null ? text : Append(text, $"quantité : {quantityText}", " — ");
    }

    /// <summary>
    /// The renewal mention. A blank value prints nothing (the ordonnance is silent on renewal, which is the
    /// default); the literal <c>"0"</c> or <c>"non"</c> is the explicit « non renouvelable »; anything else is
    /// printed as a count.
    /// </summary>
    private static string? RenewalMention(string? renewals)
    {
        var value = Trimmed(renewals);
        if (value == null)
        {
            return null;
        }

        if (value.Equals("0", StringComparison.Ordinal) || value.Equals("non", StringComparison.OrdinalIgnoreCase))
        {
            return NonRenewableMention;
        }

        return int.TryParse(value, out var times) && times == 1
            ? "Ordonnance à renouveler 1 fois."
            : $"Ordonnance à renouveler {value} fois.";
    }

    /// <summary>
    /// Parses the medications blob. The new shape is a JSON array; a pre-existing document holds a plain string,
    /// which is returned as one heading verbatim rather than dropped — and malformed JSON degrades the same way,
    /// because a prescription that renders its raw text is recoverable while one that throws is not.
    /// </summary>
    private static IReadOnlyList<PrescriptionLine> ParseLines(string? medications)
    {
        if (string.IsNullOrWhiteSpace(medications))
        {
            return Array.Empty<PrescriptionLine>();
        }

        if (!medications.TrimStart().StartsWith('['))
        {
            return new[] { new PrescriptionLine(medications.Trim(), string.Empty, string.Empty) };
        }

        List<MedicationEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<MedicationEntry>>(
                medications, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new[] { new PrescriptionLine(medications.Trim(), string.Empty, string.Empty) };
        }

        if (entries == null || entries.Count == 0)
        {
            return Array.Empty<PrescriptionLine>();
        }

        return entries
            .Select(e => new PrescriptionLine(
                FormatHeading(e.Name, e.Dosage),
                FormatPosology(e.Dose, e.TimesPerDay, e.Duration, e.DurationUnit),
                FormatDetails(e.Route, e.Quantity)))
            .ToList();
    }

    private static string Append(string text, string? part, string separator) =>
        part == null ? text : text.Length == 0 ? part : text + separator + part;

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The wire shape of one medication inside the <c>medications</c> JSON array.</summary>
    private sealed class MedicationEntry
    {
        public string? Name { get; set; }
        public string? Dosage { get; set; }
        public string? Dose { get; set; }
        public string? TimesPerDay { get; set; }
        public string? Route { get; set; }
        public string? Quantity { get; set; }
        public string? Duration { get; set; }
        public string? DurationUnit { get; set; }
        // No `Dci`: the key is still on the wire and still read by the fiche's row label, but nothing on the
        // printed sheet uses it — see FormatPosology.
    }
}
