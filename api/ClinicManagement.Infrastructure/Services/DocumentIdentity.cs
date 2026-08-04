using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>One labelled identity value on a generated document.</summary>
public sealed record IdentityLine(string Label, string Value);

/// <summary>
/// The <b>single authority</b> on the norm-mandated identity block of every generated clinical document:
/// who prescribed/certified, and for whom. Pure and deterministic so it can be unit-tested without rendering
/// a PDF.
/// <para>
/// It exists because identity had no owner. Each document type re-derived it, and the certificat proved how
/// that fails: it names the practitioner's ordre number inside its own prose because the shared header had
/// nowhere to put one — so the same legal fact was rendered in two different conventions depending on which
/// document you printed, and an <b>ordonnance carried no ordre number at all</b>, which R.5132-3 requires.
/// </para>
/// <para>
/// Every line is omitted when its value is missing. A cabinet with no email or a practitioner with no CNOMDT
/// number prints one line fewer — never a label with nothing after it, and never a bracketed placeholder.
/// </para>
/// </summary>
public static class DocumentIdentity
{
    /// <summary>
    /// The prescriber/cabinet lines, in reading order, rendered under the cabinet name in the document header.
    /// Unlabelled (except the two that need naming) because a letterhead reads as an address block, not a form.
    /// </summary>
    public static IReadOnlyList<string> PrescriberLines(MedicalDocumentPdfData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var lines = new List<string>();

        Add(lines, data.ClinicAddress);
        Add(lines, Prefixed("Tél : ", data.ClinicPhone));
        Add(lines, Prefixed("Email : ", data.ClinicEmail));

        // The practitioner's own line: name + qualité/spécialité (R.5132-3 requires the qualité).
        var practitioner = Trimmed(data.DoctorName);
        if (practitioner != null)
        {
            var specialty = Trimmed(data.DoctorSpecialty);
            lines.Add(specialty != null ? $"{practitioner} — {specialty}" : practitioner);
        }

        // The registration number that makes the prescriber identifiable. Snapshotted onto the document, so it
        // renders in the background PDF job without a live doctor lookup.
        //
        // The `doctorOrderNumber` fallback is load-bearing, not defensive: documents created before the
        // practitioner snapshot existed carry a hand-typed ordre under that key, and the certificat branch used
        // to apply this fallback itself. Dropping it while moving the number into this block would silently
        // erase the ordre from every legacy certificat.
        var ordre = !string.IsNullOrWhiteSpace(data.DoctorOrdreNumber)
            ? data.DoctorOrdreNumber
            : data.Content.GetValueOrDefault("doctorOrderNumber");
        Add(lines, Prefixed("N° CNOMDT : ", ordre));

        return lines;
    }

    /// <summary>
    /// The patient identity lines, labelled, in reading order: nom, date de naissance, sexe, poids — plus the
    /// « médecin traitant / praticien adresseur » a lettre de liaison may carry, which the norms place with the
    /// patient's identity rather than in the clinical synthèse.
    /// </summary>
    public static IReadOnlyList<IdentityLine> PatientLines(MedicalDocumentPdfData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var lines = new List<IdentityLine>();

        var name = Trimmed(data.PatientName);
        if (name != null)
        {
            lines.Add(new IdentityLine("Patient", name));
        }

        // PatientAge holds a formatted date de naissance despite its name — see the field's own remark.
        AddLine(lines, "Date de naissance", data.PatientAge);
        AddLine(lines, "Sexe", data.PatientSex);
        AddLine(lines, "Poids", Suffixed(data.PatientWeightKg, " kg"));
        AddLine(lines, "Médecin traitant / praticien adresseur", data.Content.GetValueOrDefault("medecinTraitant"));

        return lines;
    }

    private static void Add(List<string> lines, string? value)
    {
        var trimmed = Trimmed(value);
        if (trimmed != null)
        {
            lines.Add(trimmed);
        }
    }

    private static void AddLine(List<IdentityLine> lines, string label, string? value)
    {
        var trimmed = Trimmed(value);
        if (trimmed != null)
        {
            lines.Add(new IdentityLine(label, trimmed));
        }
    }

    private static string? Prefixed(string prefix, string? value)
    {
        var trimmed = Trimmed(value);
        return trimmed == null ? null : prefix + trimmed;
    }

    /// <summary>
    /// Appends a unit only when the value does not already carry one — a dentist who types « 32 kg » must not
    /// get « 32 kg kg », and one who types « 32 » should still read as kilograms.
    /// </summary>
    private static string? Suffixed(string? value, string suffix)
    {
        var trimmed = Trimmed(value);
        if (trimmed == null)
        {
            return null;
        }

        return trimmed.EndsWith("kg", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + suffix;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
