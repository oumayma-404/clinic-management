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
    /// <summary>
    /// The mandatory deontological mention rendered above the signature block. It carries both halves the CNOM
    /// requires: the <b>remise en main propre</b> (a certificate is handed to the patient, never to a third
    /// party) <b>and</b> the <b>finality</b> — « pour faire valoir ce que de droit » is what states the
    /// certificate is issued for whatever use the patient lawfully needs, rather than for a purpose the
    /// practitioner has vouched for.
    /// </summary>
    public const string MandatoryMention =
        "Certificat établi à la demande de l'intéressé(e) et remis en main propre pour faire valoir ce que de droit.";

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
        string patientName,
        string? patientDobFormatted,
        string? objetMotif,
        string? duration,
        string? startDateFormatted)
    {
        var specialty = string.IsNullOrWhiteSpace(doctorSpecialty) ? "médecin dentiste" : doctorSpecialty!.Trim();
        var dob = string.IsNullOrWhiteSpace(patientDobFormatted) ? "[JJ/MM/AAAA]" : patientDobFormatted!.Trim();

        // The attestation formula, which is what makes this a certificate: it names the registering body (the
        // legal form) and states that the practitioner personally examined the patient — the « faits médicaux
        // personnellement constatés » rule.
        //
        // ⚠️ It deliberately no longer repeats the ordre NUMBER or the cabinet address: both now render in the
        // shared identity block (DocumentIdentity), which every document type carries. They lived here because
        // the header had nowhere to put them — which is also why an ordonnance carried no ordre number at all.
        // Naming the body while printing the number once is what keeps the formula legally intact without
        // stating the same fact twice on one page.
        var paragraphs = new List<string>
        {
            $"Je soussigné(e), Docteur {doctorName}, {specialty}, inscrit(e) à l'{OrdreLabel}, " +
            $"certifie avoir examiné ce jour {patientName}, né(e) le {dob}."
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
