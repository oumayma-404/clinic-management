using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Feedback;

/// <summary>
/// The closed vocabularies of « Donner mon avis » and the week-one question (<c>guided-first-days</c> E), and when
/// that question may be put. Strings on the wire, parsed here — never raw enums.
///
/// <para>⚠️ <see cref="Areas"/> is mirrored by <c>web/lib/feedback.ts</c>; <c>FeedbackAreasMirrorTests</c> holds the
/// two equal, so the form cannot offer an area the server refuses.</para>
/// </summary>
public static class FeedbackRules
{
    public const string KindIdea = "idee";
    public const string KindProblem = "probleme";
    public const string KindQuestion = "question";
    public const string KindReview = "avis";

    public static readonly IReadOnlyList<string> Kinds = [KindIdea, KindProblem, KindQuestion, KindReview];

    public static readonly IReadOnlyDictionary<string, string> KindLabels = new Dictionary<string, string>
    {
        [KindIdea] = "Idée",
        [KindProblem] = "Problème",
        [KindQuestion] = "Question",
        [KindReview] = "Avis",
    };

    /// <summary>The week-one card, or « Aide » → « Donner mon avis ou une idée ».</summary>
    public const string SourceWeekOne = "semaine-1";
    public const string SourceHelp = "aide";

    public static readonly IReadOnlyList<string> Sources = [SourceWeekOne, SourceHelp];

    public static readonly IReadOnlyList<string> Areas =
    [
        "agenda", "dossier-patient", "odontogramme", "fiche-de-soins", "devis", "caisse", "documents", "telephone",
    ];

    public static readonly IReadOnlyDictionary<string, string> AreaLabels = new Dictionary<string, string>
    {
        ["agenda"] = "Agenda",
        ["dossier-patient"] = "Dossier patient",
        ["odontogramme"] = "Odontogramme",
        ["fiche-de-soins"] = "Fiche de soins",
        ["devis"] = "Devis",
        ["caisse"] = "Caisse",
        ["documents"] = "Documents",
        ["telephone"] = "Application téléphone",
    };

    /// <summary>The question is put to a person whose account is at least this old…</summary>
    public const int MinAccountDays = 7;

    /// <summary>…in a cabinet that has really worked on this many distinct days…</summary>
    public const int MinActiveDays = 3;

    /// <summary>…counted over this many recent days.</summary>
    public const int ActiveDaysLookback = 60;

    /// <summary>The author's role in French, for the notice and « Retours » (an unknown role passes through).</summary>
    public static string RoleLabel(string? role) => User.NormalizeRole(role) switch
    {
        User.RoleAdmin => "Administrateur",
        User.RoleDoctor => "Médecin",
        User.RoleSecretary => "Secrétaire",
        _ => role ?? string.Empty,
    };

    public static string? ParseKind(string? value) => Match(value, Kinds);

    public static string? ParseSource(string? value) => Match(value, Sources);

    /// <summary>The areas, canonical and de-duplicated, or <c>null</c> when any value is not one of <see cref="Areas"/>.</summary>
    public static IReadOnlyList<string>? ParseAreas(IEnumerable<string>? values)
    {
        var parsed = new List<string>();
        foreach (var value in values ?? [])
        {
            var area = Match(value, Areas);
            if (area is null) return null;
            if (!parsed.Contains(area)) parsed.Add(area);
        }

        return parsed;
    }

    private static string? Match(string? value, IReadOnlyList<string> set)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : set.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
