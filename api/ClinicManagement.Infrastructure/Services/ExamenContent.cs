using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>One requested examen as it is printed on the demande d'examens.</summary>
public sealed record ExamenLine(string Text);

/// <summary>The composed body: the opening formula and the requested examens, in the order they were entered.</summary>
public sealed record ExamenBody(string Intro, IReadOnlyList<ExamenLine> Lines);

/// <summary>
/// Builds the body of a « demande d'examens » from its <c>ContentJson</c> — the sibling of
/// <see cref="PrescriptionContent"/>, <see cref="LiaisonContent"/> and <see cref="CertificatTextBuilder"/>,
/// pure and deterministic so it is unit-testable without rendering a PDF.
///
/// <para>
/// ⚠️ <b>There is no per-line formatting here, and there must never be any.</b> An examen carries exactly one
/// value — what the practitioner wrote — and it is printed verbatim: « Radiographie panoramique dentaire »,
/// « Bilan sanguin : NFS, glycémie, TP/INR ». That is not a simplification of
/// <see cref="PrescriptionContent.FormatLine"/>, it is a different kind of instruction: a médicament line is
/// assembled from six norm-ordered parts (le dosage, la posologie, la voie, la durée, la quantité, la DCI) and
/// an examen has none of them. Adding a dosage or a posologie to this file would be inventing a clinical field
/// nobody prescribes.
/// </para>
///
/// <para>
/// ⚠️ <b>No renouvellement mention either</b>, unlike the médicament ordonnance. Renewal is a dispensing
/// concept: an examen prescription is single-use by default, which is precisely one of the reasons the two
/// cannot share a sheet. See <c>DocumentTypes.Examens</c>.
/// </para>
/// </summary>
public static class ExamenContent
{
    /// <summary>
    /// The opening formula, and the one place it lives on the server. Its twin is <c>EXAMENS_INTRO</c> in
    /// <c>web/lib/documents.ts</c>, and <c>check:responsive</c>'s <c>examens-intro-has-one-owner</c> compares
    /// the two in both directions — a demande d'examens whose printed sentence differs from the one the app
    /// showed is the same class of drift the prescription line has three guards against.
    /// </summary>
    public const string IntroPlural = "Prière de bien vouloir faire pratiquer les examens suivants :";

    /// <summary>Singular form. « les examens suivants » above a single line reads as though one were missing.</summary>
    public const string IntroSingular = "Prière de bien vouloir faire pratiquer l'examen suivant :";

    /// <summary>The <c>ContentJson</c> key holding the requested examens, as an array of <c>{ name }</c>.</summary>
    public const string ExamensKey = "examens";

    public static ExamenBody Build(IReadOnlyDictionary<string, string> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = ParseLines(content.GetValueOrDefault(ExamensKey));
        return new ExamenBody(lines.Count == 1 ? IntroSingular : IntroPlural, lines);
    }

    /// <summary>
    /// Parses the examens blob. Malformed JSON, or a plain string from some future caller, degrades to one
    /// verbatim line rather than throwing — for <c>PrescriptionContent.ParseLines</c>' reason: a
    /// prescription that renders its raw text is recoverable, one that throws is not.
    /// </summary>
    private static IReadOnlyList<ExamenLine> ParseLines(string? examens)
    {
        if (string.IsNullOrWhiteSpace(examens))
        {
            return Array.Empty<ExamenLine>();
        }

        if (!examens.TrimStart().StartsWith('['))
        {
            return new[] { new ExamenLine(examens.Trim()) };
        }

        List<ExamenEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ExamenEntry>>(
                examens, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new[] { new ExamenLine(examens.Trim()) };
        }

        if (entries == null || entries.Count == 0)
        {
            return Array.Empty<ExamenLine>();
        }

        return entries
            .Select(e => e.Name?.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => new ExamenLine(name!))
            .ToList();
    }

    /// <summary>The wire shape of one entry inside the <c>examens</c> JSON array. One field, by design.</summary>
    private sealed class ExamenEntry
    {
        public string? Name { get; set; }
    }
}
