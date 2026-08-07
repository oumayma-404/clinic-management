using System.Text.Json;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// The mandatory-field gate for a <c>bulletin-cnam</c>, shared by the create and update handlers.
/// </summary>
/// <remarks>
/// <para>
/// Before this, the two document handlers validated exactly two things — « honoraires is retired » and
/// « a liaison needs a recipient ». A bulletin was saveable with no identifiant, no régime, no lien, no act and
/// no code conventionnel, and <b>every one of those degraded silently by design</b>: the renderer's
/// <c>DrawLeft</c> returns on a blank string and its régime/lien <c>switch</c>es tick nothing for a value they
/// do not recognise. That behaviour is <i>correct for a renderer</i> — a drawing routine that throws turns a
/// missing field into a failed PDF — so the check belongs here, at the write. The renderer was left alone.
/// </para>
/// <para>
/// ⚠️ <b>The régime and lien values are compared against <see cref="CnamInfo"/>'s constants, not against
/// literals retyped here.</b> That is the whole point of the constants existing: the renderer ticks the box by
/// matching the same strings, so « convention bilaterale » (lower case, no accent) is now a refusal naming the
/// field rather than a form printed with an empty régime box.
/// </para>
/// <para>
/// Reads its inputs out of the document's <c>ContentJson</c> rather than taking new command properties: the
/// bulletin's fields already round-trip through that dictionary (the editor builds it, the renderer reads it),
/// and adding a parallel typed payload would create a second place a field can be present in one and absent in
/// the other.
/// </para>
/// </remarks>
public static class BulletinCnamValidation
{
    // ContentJson keys, as written by the editor's buildBulletinContent and read by CnamBs1BulletinRenderer.
    private const string KeyIdentifiantUnique = "identifiantUnique";
    private const string KeyRegime = "regime";
    private const string KeyMaladeLien = "maladeLien";
    private const string KeyMaladeLienRang = "maladeLienRang";
    private const string KeyActs = "acts";
    private const string KeyDoctorCodeProfessionnel = "doctorCodeProfessionnel";

    /// <summary>
    /// <c>null</c> when the bulletin can be submitted, otherwise one French message naming <b>every</b> missing
    /// or unusable field.
    /// </summary>
    /// <remarks>
    /// All the problems at once, deliberately: a dentist filling a CNAM form should not discover the five
    /// mandatory fields one refusal at a time. The editor shows the same list before Save is reachable, so this
    /// is the backstop rather than the primary channel.
    /// </remarks>
    public static string? Validate(string? contentJson)
    {
        var content = ParseContent(contentJson);
        if (content == null)
        {
            return "Le contenu du bulletin de soins est illisible. Rouvrez le bulletin et enregistrez-le à nouveau.";
        }

        var problems = new List<string>();

        var identifiant = Get(content, KeyIdentifiantUnique);
        if (string.IsNullOrWhiteSpace(identifiant))
        {
            problems.Add("l'identifiant unique CNAM du patient est absent de sa fiche");
        }
        else if (!CnamInfo.IsValidIdentifiantUnique(identifiant))
        {
            // K7: the renderer combs one digit per printed cell and used to drop the rest without a trace.
            problems.Add(
                $"l'identifiant unique CNAM ne tient pas dans le formulaire "
                + $"({CnamInfo.CountIdentifiantDigits(identifiant)} chiffres pour "
                + $"{CnamInfo.IdentifiantUniqueDigits} cases) — corrigez-le sur la fiche du patient");
        }

        var regime = Get(content, KeyRegime);
        if (string.IsNullOrWhiteSpace(regime))
        {
            problems.Add($"le régime est absent ({FormatChoices(CnamInfo.AllowedRegimes)})");
        }
        else if (!CnamInfo.IsKnownRegime(regime))
        {
            problems.Add($"le régime « {regime} » n'est pas reconnu ({FormatChoices(CnamInfo.AllowedRegimes)})");
        }

        var lien = Get(content, KeyMaladeLien);
        if (string.IsNullOrWhiteSpace(lien))
        {
            problems.Add($"le lien de parenté est absent ({FormatChoices(CnamInfo.AllowedLiens)})");
        }
        else if (!CnamInfo.IsKnownLien(lien))
        {
            problems.Add($"le lien de parenté « {lien} » n'est pas reconnu ({FormatChoices(CnamInfo.AllowedLiens)})");
        }
        else if (CnamInfo.LienRequiresRang(lien) && string.IsNullOrWhiteSpace(Get(content, KeyMaladeLienRang)))
        {
            problems.Add($"le rang est obligatoire pour le lien « {lien} »");
        }

        if (CountActs(Get(content, KeyActs)) == 0)
        {
            problems.Add("le bulletin ne porte aucun acte");
        }

        if (string.IsNullOrWhiteSpace(Get(content, KeyDoctorCodeProfessionnel)))
        {
            problems.Add(
                "le code conventionnel du praticien traitant est absent — renseignez-le dans « Mon profil »");
        }

        return problems.Count == 0
            ? null
            : $"Bulletin de soins incomplet : {string.Join(" ; ", problems)}.";
    }

    // The acts ride as a JSON *string* inside ContentJson (the editor stringifies its array), so this parses one
    // level deeper. A malformed payload counts as zero acts rather than throwing: the renderer already treats it
    // that way, and a bulletin whose acts could not be read must be refused, not saved as an act-less form.
    private static int CountActs(string? actsJson)
    {
        if (string.IsNullOrWhiteSpace(actsJson))
        {
            return 0;
        }

        try
        {
            using var parsed = JsonDocument.Parse(actsJson);
            return parsed.RootElement.ValueKind == JsonValueKind.Array
                ? parsed.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static Dictionary<string, string?>? ParseContent(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            // An empty content is not "unreadable" — it is a bulletin with nothing filled in, and the field-by-
            // field messages below say so far more usefully than « illisible » would.
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

    private static string FormatChoices(IReadOnlyList<string> choices)
        => string.Join(" / ", choices);
}
