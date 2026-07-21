namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Builds the ordered, non-empty sections of a lettre de liaison body (FR-4, Part E). Pure and
/// deterministic so it can be unit-tested without rendering a PDF (LIA-4). New letters use guided fields
/// (motif, examen clinique, examen radiologique, actes réalisés, prescriptions); a legacy letter (pre-Part-E)
/// carries only a free-text <c>content</c> body, rendered as one unlabelled section when no guided field is
/// present (LIA-5). Empty fields are omitted entirely — no empty headings.
/// </summary>
public static class LiaisonContent
{
    // Ordered guided sections: (rendered heading, ContentJson key). Order is the reading order on the letter.
    private static readonly (string Heading, string Key)[] GuidedFields =
    {
        ("Motif", "motif"),
        ("Examen clinique", "examenClinique"),
        ("Examen radiologique", "examenRadiologique"),
        ("Actes réalisés", "actesRealises"),
        ("Prescriptions", "prescriptions"),
    };

    public static IReadOnlyList<LiaisonSection> Build(IReadOnlyDictionary<string, string> content)
    {
        var sections = new List<LiaisonSection>();
        foreach (var (heading, key) in GuidedFields)
        {
            if (content.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                sections.Add(new LiaisonSection(heading, value.Trim()));
            }
        }

        // Legacy free-text body (pre-Part-E letters) — rendered only when no guided field is present, so an
        // old liaison stays readable while new letters use the structured fields above.
        if (sections.Count == 0
            && content.TryGetValue("content", out var legacy)
            && !string.IsNullOrWhiteSpace(legacy))
        {
            sections.Add(new LiaisonSection(null, legacy.Trim()));
        }

        return sections;
    }
}

/// <summary>One rendered liaison section: an optional bold heading + its body (a null heading = legacy free text).</summary>
public sealed record LiaisonSection(string? Heading, string Body);
