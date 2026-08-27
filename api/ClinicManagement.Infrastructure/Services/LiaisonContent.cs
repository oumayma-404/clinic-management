namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Builds the ordered, non-empty sections of a lettre de liaison body. Pure and deterministic so it can be
/// unit-tested without rendering a PDF.
/// <para>
/// The <c>content</c> key is the letter's <b>primary free-text body</b> — the doctor's own prose — and is a
/// first-class section rendered in its normal place, unlabelled so it reads as a letter rather than a form
/// answer. It used to render <b>only</b> when no guided field was filled, which made prose and structure
/// mutually exclusive: a new letter could not carry a sentence the doctor wrote. That condition is gone.
/// </para>
/// <para>
/// The reading order below follows the norms a lettre de liaison must satisfy (décret n° 2016-995 du
/// 20 juillet 2016 + HAS): motif, synthèse clinique, traitement en cours et allergies connues, prescriptions,
/// résultats d'examens en attente, consignes de suivi. Every section is optional — an empty field is omitted
/// entirely, so no empty heading is ever printed, and nothing here is ever required of the practitioner.
/// </para>
/// <para>
/// ⚠️ « Médecin traitant / praticien adresseur » is deliberately <b>not</b> a section: the norms place the
/// identity of the professionals involved alongside the patient's, so it is rendered in the identity block by
/// <c>PdfGenerationService</c>, not in the body.
/// </para>
/// </summary>
public static class LiaisonContent
{
    /// <summary>
    /// The letter's body in reading order: (rendered heading — null = unlabelled prose, ContentJson key).
    /// </summary>
    private static readonly (string? Heading, string Key)[] Sections =
    {
        ("Motif de la liaison", "motif"),
        (null, "content"),
        ("Examen clinique", "examenClinique"),
        ("Examen radiologique", "examenRadiologique"),
        ("Actes réalisés", "actesRealises"),
        ("Traitement en cours et allergies connues", "traitementEnCours"),
        ("Prescriptions", "prescriptions"),
        ("Résultats d'examens en attente", "examensEnAttente"),
        ("Consignes de suivi / avis attendu", "consignesSuivi"),
        ("Pièces jointes", "piecesJointes"),
    };

    public static IReadOnlyList<LiaisonSection> Build(IReadOnlyDictionary<string, string> content)
    {
        var sections = new List<LiaisonSection>();
        foreach (var (heading, key) in Sections)
        {
            if (content.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                sections.Add(new LiaisonSection(heading, value.Trim()));
            }
        }

        return sections;
    }
}

/// <summary>One rendered liaison section: an optional bold heading + its body (a null heading = free-text prose).</summary>
public sealed record LiaisonSection(string? Heading, string Body);
