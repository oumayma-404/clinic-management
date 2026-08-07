using System.Globalization;
using System.Text.Json;

namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// The mandatory-field gate for an <c>arret-travail</c>, shared by the create and update handlers — the sibling of
/// <see cref="BulletinCnamValidation"/>, applying the same K-series lesson from the start rather than after the
/// first rejected form (L11).
/// </summary>
/// <remarks>
/// <para>
/// The rule the bulletin taught: <b>a renderer must not validate</b>. <c>CnamArretTravailRenderer</c> skips a blank
/// field, ticks nothing for a value it does not recognise and prints whatever it is handed — which is the correct
/// behaviour for a drawing routine, because one that throws turns a missing field into a failed PDF. The
/// consequence is that every omission degrades <i>silently</i>: an arrêt with no duration prints as a certificate
/// entitling the patient to nothing, and reports success. So the check lives here, at the write.
/// </para>
/// <para>
/// ⚠️ <b>The motif is deliberately not required and deliberately not printed.</b> P 061's practitioner half carries
/// no diagnosis field at all — the form's own « partie confidentielle au verso » is where a medical reason goes, and
/// the front is read by an employer. Requiring a motif here would demand a value with nowhere to go; printing one
/// would put a diagnosis on the copy the patient hands to their employer. It is kept in the content because the
/// clinic's own record of *why* is worth having.
/// </para>
/// <para>
/// ⚠️ <b>The practitioner's identity is validated, not defaulted.</b> The K-series defect was a bulletin stamped
/// with <c>doctors[0]</c>'s code conventionnel — a real code, belonging to the wrong dentist, on every act row. Here
/// the name and at least one of code conventionnel / n° CONSEIL DE L'ORDRE are required, and the editor makes the
/// practitioner an explicit choice.
/// </para>
/// </remarks>
public static class ArretTravailValidation
{
    /// <summary>
    /// <c>null</c> when the certificate can be issued, otherwise one French message naming <b>every</b> missing or
    /// unusable field — all at once, for the same reason as the bulletin's: a practitioner should not discover the
    /// mandatory fields one refusal at a time.
    /// </summary>
    public static string? Validate(string? contentJson)
    {
        var content = ParseContent(contentJson);
        if (content == null)
        {
            return "Le contenu de l'arrêt de travail est illisible. Rouvrez le document et enregistrez-le à nouveau.";
        }

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Get(content, ArretTravailKeys.PatientLastName)))
        {
            problems.Add("le nom du patient est absent");
        }

        // The duration IS the document: an arrêt de travail with no number of days certifies nothing.
        var days = Get(content, ArretTravailKeys.Days);
        if (string.IsNullOrWhiteSpace(days))
        {
            problems.Add("la durée de l'arrêt (en jours) est absente");
        }
        else if (!int.TryParse(days, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayCount)
                 || dayCount <= 0)
        {
            problems.Add($"la durée « {days} » n'est pas un nombre de jours valide");
        }
        else if (dayCount > ArretTravailKeys.MaxDays)
        {
            problems.Add(
                $"la durée de {dayCount} jours dépasse le maximum accepté ({ArretTravailKeys.MaxDays} jours) — "
                + "vérifiez la saisie");
        }

        if (string.IsNullOrWhiteSpace(Get(content, ArretTravailKeys.FromDate)))
        {
            problems.Add("la date de début de l'arrêt est absente");
        }

        if (string.IsNullOrWhiteSpace(Get(content, ArretTravailKeys.DoctorName)))
        {
            problems.Add("le praticien traitant n'est pas désigné");
        }

        // At least one identifier, not both: a conventionné dentist has a code conventionnel, and one who is not
        // still has a CNOMDT ordre number. Requiring both would refuse a legitimate practitioner; requiring neither
        // prints a certificate the caisse cannot attribute.
        var code = Get(content, ArretTravailKeys.DoctorCodeConventionnel);
        var ordre = Get(content, ArretTravailKeys.DoctorOrdreNumber);
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(ordre))
        {
            problems.Add(
                "le code conventionnel ou le n° au Conseil de l'Ordre du praticien est absent — "
                + "renseignez-le dans « Mon profil »");
        }

        // « Sorties autorisées » is one statement made of a box and two hours. Half of it is worse than none: the
        // caisse reads an empty hour slot beside a ticked box as the answer.
        var outFrom = Get(content, ArretTravailKeys.OutingsFrom);
        var outTo = Get(content, ArretTravailKeys.OutingsTo);
        if (string.IsNullOrWhiteSpace(outFrom) != string.IsNullOrWhiteSpace(outTo))
        {
            problems.Add("les sorties autorisées demandent une heure de début **et** une heure de fin");
        }

        var trauma = Get(content, ArretTravailKeys.TraumaCause);
        if (!string.IsNullOrWhiteSpace(trauma) && !ArretTravailKeys.AllowedTraumaCauses.Contains(trauma, StringComparer.Ordinal))
        {
            problems.Add($"la cause du traumatisme « {trauma} » n'est pas reconnue");
        }

        return problems.Count == 0
            ? null
            : $"Arrêt de travail incomplet : {string.Join(" ; ", problems)}.";
    }

    /// <summary>
    /// Same parser as the bulletin's, and deliberately a copy rather than a shared helper: it is fifteen lines of
    /// JSON-to-string flattening with no decision in it, and the two validators are the only readers. Extracting it
    /// would create a shared type whose only purpose is to be shared.
    /// </summary>
    private static Dictionary<string, string?>? ParseContent(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            // Not "unreadable" — an empty content is a document with nothing filled in, and the field-by-field
            // messages above say so far more usefully.
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            using var parsed = JsonDocument.Parse(contentJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => property.Value.GetRawText(),
                };
            }

            return values;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Get(Dictionary<string, string?> content, string key)
        => content.TryGetValue(key, out var value) ? value?.Trim() : null;
}
