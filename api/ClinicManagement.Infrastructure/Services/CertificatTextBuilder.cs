namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Builds the body text of a certificat médical (FR-2, Part D). Pure and deterministic so it can be
/// unit-tested without rendering a PDF (REND-1/REND-2, CERT-2/CERT-3). The certificat is lightly
/// generalized: a free objet/motif body with the repos médical block as one *optional* clause (rendered
/// only when a rest duration is present). It always carries the mandatory deontological mention above the
/// signature (FR-2.3) and the CNOMDT ordre label (FR-2.4) — never the old "Ordre des Médecins".
/// </summary>
public static class CertificatTextBuilder
{
    /// FR-2.3 — the mandatory deontological mention rendered above the signature block.
    public const string MandatoryMention =
        "Certificat établi à la demande de l'intéressé(e) et remis en main propre.";

    /// FR-2.4 — the ordre label (replaces the old, incorrect "Ordre des Médecins").
    public const string OrdreLabel = "Ordre National des Médecins Dentistes (CNOMDT)";

    /// <summary>
    /// Compose the certificat body. All date values are expected pre-formatted (dd/MM/yyyy); empty optional
    /// inputs are omitted from the output rather than rendered as placeholders (except the identity line,
    /// which keeps bracketed placeholders when a value is genuinely missing).
    /// </summary>
    public static CertificatText Build(
        string doctorName,
        string? doctorSpecialty,
        string? ordreNumber,
        string? clinicAddress,
        string patientName,
        string? patientDobFormatted,
        string? objetMotif,
        string? duration,
        string? startDateFormatted)
    {
        var specialty = string.IsNullOrWhiteSpace(doctorSpecialty) ? "médecin dentiste" : doctorSpecialty!.Trim();
        var ordre = string.IsNullOrWhiteSpace(ordreNumber) ? "[Numéro]" : ordreNumber!.Trim();
        var address = string.IsNullOrWhiteSpace(clinicAddress) ? "[Adresse]" : clinicAddress!.Trim();
        var dob = string.IsNullOrWhiteSpace(patientDobFormatted) ? "[JJ/MM/AAAA]" : patientDobFormatted!.Trim();

        var paragraphs = new List<string>
        {
            $"Je soussigné(e), Docteur {doctorName}, {specialty}, inscrit(e) à l'{OrdreLabel} sous le n° {ordre}, " +
            $"exerçant à {address}, certifie avoir examiné ce jour {patientName}, né(e) le {dob}."
        };

        // FR-2.1: the free objet/motif body (présence, soins en cours, aptitude…). Rendered only when filled.
        if (!string.IsNullOrWhiteSpace(objetMotif))
        {
            paragraphs.Add(objetMotif!.Trim());
        }

        // FR-2.1: the repos médical clause is one *optional* use — rendered only when a rest duration is set.
        if (!string.IsNullOrWhiteSpace(duration))
        {
            var plural = int.TryParse(duration, out var days) && days > 1 ? "s" : "";
            var repos = $"Son état de santé nécessite un repos médical d'une durée de {duration!.Trim()} jour{plural}";
            if (!string.IsNullOrWhiteSpace(startDateFormatted))
            {
                repos += $" à compter du {startDateFormatted!.Trim()}";
            }
            repos += ".";
            paragraphs.Add(repos);
        }

        return new CertificatText(paragraphs, MandatoryMention);
    }
}

/// <summary>The composed certificat text: body paragraphs plus the mandatory deontological mention.</summary>
public sealed record CertificatText(IReadOnlyList<string> BodyParagraphs, string Mention)
{
    /// The full rendered text (body paragraphs followed by the mandatory mention) — convenient for assertions.
    public string FullText => string.Join(" ", BodyParagraphs.Concat(new[] { Mention }));
}
