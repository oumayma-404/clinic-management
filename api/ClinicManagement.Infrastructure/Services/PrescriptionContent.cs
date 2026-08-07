using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>One prescribed line as it is printed on the ordonnance.</summary>
public sealed record PrescriptionLine(string Text);

/// <summary>The composed ordonnance body: the prescribed lines plus the optional renewal mention.</summary>
public sealed record PrescriptionBody(IReadOnlyList<PrescriptionLine> Lines, string? RenewalMention);

/// <summary>
/// Builds the body of an ordonnance from its <c>ContentJson</c>. Pure and deterministic so it can be
/// unit-tested without rendering a PDF — the sibling of <see cref="LiaisonContent"/> and
/// <see cref="CertificatTextBuilder"/>, and the reason the prescription's formatting is no longer ~60 lines
/// inlined in the renderer's switch.
/// <para>
/// Each line carries what R.5132-3 requires of a prescription: the médicament (or its DCI), the posologie,
/// the <b>voie d'administration</b>, the <b>quantité</b> and the durée — the middle two being new, since a
/// posology with no route and no quantity is not a dispensable instruction. The <b>renouvellement</b> mention
/// is per-ordonnance, not per line: it governs the document, and printing it against one médicament would
/// read as applying to that one only.
/// </para>
/// Every added element is optional and omitted when unset, so a legacy prescription renders exactly as before.
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
    /// Formats one medication into its printed line. Public so the renderer's legacy path and the tests share
    /// the exact formatting rather than re-deriving it.
    /// </summary>
    public static string FormatLine(
        string? name,
        string? dosage,
        string? timesPerDay,
        string? route,
        string? quantity,
        string? duration,
        IReadOnlyList<string>? dci)
    {
        var text = Trimmed(name) ?? "Médicament";

        var dosageText = Trimmed(dosage);
        if (dosageText != null)
        {
            text += $" {dosageText}";
        }

        var frequency = Trimmed(timesPerDay);
        if (frequency != null)
        {
            text += $", {frequency}x par jour";
        }

        // Voie d'administration — « par voie orale », « en application locale »… printed as entered so a
        // dentist is not boxed into a closed list the norms do not define.
        var routeText = Trimmed(route);
        if (routeText != null)
        {
            text += $", {routeText}";
        }

        var durationText = Trimmed(duration);
        if (durationText != null)
        {
            var isPlural = int.TryParse(durationText, out var days) && days > 1;
            text += $" pendant {durationText} jour{(isPlural ? "s" : "")}";
        }

        // Quantité — how much to dispense (boîtes / unités), which is what makes the line fillable.
        var quantityText = Trimmed(quantity);
        if (quantityText != null)
        {
            text += $" — quantité : {quantityText}";
        }

        if (dci != null)
        {
            var dciText = string.Join(", ", dci.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()));
            if (!string.IsNullOrWhiteSpace(dciText))
            {
                text += $" (DCI : {dciText})";
            }
        }

        return text;
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
    /// which is returned as one line verbatim rather than dropped — and malformed JSON degrades the same way,
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
            return new[] { new PrescriptionLine(medications.Trim()) };
        }

        List<MedicationEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<MedicationEntry>>(
                medications, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new[] { new PrescriptionLine(medications.Trim()) };
        }

        if (entries == null || entries.Count == 0)
        {
            return Array.Empty<PrescriptionLine>();
        }

        return entries
            .Select(e => new PrescriptionLine(
                FormatLine(e.Name, e.Dosage, e.TimesPerDay, e.Route, e.Quantity, e.Duration, e.Dci)))
            .ToList();
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The wire shape of one medication inside the <c>medications</c> JSON array.</summary>
    private sealed class MedicationEntry
    {
        public string? Name { get; set; }
        public string? Dosage { get; set; }
        public string? TimesPerDay { get; set; }
        public string? Route { get; set; }
        public string? Quantity { get; set; }
        public string? Duration { get; set; }
        public List<string>? Dci { get; set; }
    }
}
