using ClinicManagement.Application.Features.Documents;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>One run of the certificat's sentence. <see cref="Bold"/> marks the facts a reader must find.</summary>
public sealed record CertificatSegment(string Text, bool Bold = false);

/// <summary>
/// Builds the body text of a certificat médical. Pure and deterministic so it can be unit-tested without
/// rendering a PDF.
/// <para>
/// ⚠️ <b>The certificat is ONE sentence</b>, and it is the practice owner's own wording: « Je soussigné(e)
/// Docteur … certifie avoir examiné ce jour M. <b>Nom</b> et atteste que son état de santé nécessite un repos
/// de <b>5 jours</b> à compter du <b>21/07/2026</b>, sauf complications. » What it used to carry and no longer
/// does — the spécialité, « inscrit(e) à l'Ordre National … », the patient's date de naissance, a free
/// objet/motif and the deontological « remis en main propre » mention — was withdrawn deliberately, not lost:
/// the practitioner and the cabinet are already identified in the shared letterhead
/// (<see cref="DocumentIdentity"/>).
/// </para>
/// <para>
/// ⚠️ <b>Four facts are bold, and the list is exactly what a reader has to find</b>: the patient's name, the
/// count, its unit and the start date. The certificat carries <b>no patient identity block</b> either (unlike
/// every other type) — the name is in the sentence, and printing it twice made the paper read as a form.
/// </para>
/// <para>
/// ⚠️ A stored <c>objetMotif</c> is still printed when a legacy certificat carries one. The field is gone from
/// the editor, so nothing new writes it — but re-rendering an issued certificate must not silently drop a
/// paragraph a practitioner wrote.
/// </para>
/// </summary>
public static class CertificatTextBuilder
{
    /// <summary>Printed when the patient's civilité is unknown, so the practitioner can strike one out.</summary>
    public const string UnknownCivility = "M./Mme";

    /// <summary>
    /// Compose the certificat body. All date values are expected pre-formatted (dd/MM/yyyy); empty optional
    /// inputs are omitted from the output rather than rendered as placeholders.
    /// </summary>
    public static CertificatText Build(
        string doctorName,
        string patientName,
        string? patientCivility,
        string? objetMotif,
        string? duration,
        string? durationUnit,
        string? startDateFormatted)
    {
        var civility = string.IsNullOrWhiteSpace(patientCivility) ? UnknownCivility : patientCivility!.Trim();
        var sentence = new List<CertificatSegment>
        {
            new($"Je soussigné(e) Docteur {doctorName}, certifie avoir examiné ce jour {civility} "),
            new(patientName, Bold: true),
        };

        if (!string.IsNullOrWhiteSpace(duration))
        {
            var count = duration!.Trim();
            var unit = DurationUnits.Label(durationUnit, count);
            sentence.Add(new(", et atteste que son état de santé nécessite un repos de "));
            sentence.Add(new($"{count} {unit}", Bold: true));

            if (!string.IsNullOrWhiteSpace(startDateFormatted))
            {
                sentence.Add(new(" à compter du "));
                sentence.Add(new(startDateFormatted!.Trim(), Bold: true));
            }

            sentence.Add(new(", sauf complications"));
        }

        sentence.Add(new("."));

        var paragraphs = new List<IReadOnlyList<CertificatSegment>> { sentence };

        if (!string.IsNullOrWhiteSpace(objetMotif))
        {
            paragraphs.Add(new[] { new CertificatSegment(objetMotif!.Trim()) });
        }

        return new CertificatText(paragraphs);
    }
}

/// <summary>The composed certificat text: one paragraph per run list.</summary>
public sealed record CertificatText(IReadOnlyList<IReadOnlyList<CertificatSegment>> BodyParagraphs)
{
    /// The full rendered text, formatting dropped — convenient for assertions.
    public string FullText =>
        string.Join(" ", BodyParagraphs.Select(p => string.Concat(p.Select(s => s.Text))));
}
