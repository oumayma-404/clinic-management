namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// What a line of an ordonnance <b>is</b> — a médicament, or an examen the patient is being sent for (bilan,
/// radio, whatever the practitioner writes). Persisted as the <c>kind</c> key on each entry of the
/// prescription's <c>content.medications</c> array.
///
/// <para>
/// ⚠️ <b>The renderer never reads it, and that is the point.</b> An examen is a line carrying only a
/// <c>name</c>, so <c>PrescriptionContent.FormatLine</c> already prints it correctly — verbatim, as its own
/// line under « Prescription: » — without a branch. The kind exists for the two surfaces that must tell the
/// two apart *before* printing: the fiche's own editor (which offers a catalogue + posologie for one and a
/// free field for the other) and the séance history line. Adding it to the printed format would mean editing
/// the three byte-identical line formatters this product already carries, for no change on the paper.
/// </para>
///
/// <para>
/// ⚠️ <b>An absent kind is a médicament.</b> Every ordonnance written before this key existed has none, and
/// every one of those lines is a drug. <see cref="Normalize"/> is the only place that decision lives, so a
/// reader cannot forget it.
/// </para>
/// </summary>
public static class PrescriptionLineKinds
{
    /// <summary>A drug: catalogue entry or free text, with an optional posologie.</summary>
    public const string Medicament = "medicament";

    /// <summary>An examen requested — « Radiographie panoramique », « Bilan sanguin : NFS, glycémie ».</summary>
    public const string Examen = "examen";

    /// <summary>The JSON key each line carries inside <c>content.medications</c>.</summary>
    public const string Key = "kind";

    /// <summary>
    /// The kind a stored line actually has: <see cref="Examen"/> only when it says so, and
    /// <see cref="Medicament"/> for null, blank, and anything unrecognised — a legacy line, or one written by
    /// a build that knows a kind this one does not.
    /// </summary>
    public static string Normalize(string? kind) =>
        string.Equals(kind?.Trim(), Examen, StringComparison.OrdinalIgnoreCase)
            ? Examen
            : Medicament;

    /// <summary>True when the line is an examen request rather than a drug.</summary>
    public static bool IsExamen(string? kind) => Normalize(kind) == Examen;
}
