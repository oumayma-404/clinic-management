namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// The <c>ContentJson</c> keys of an <c>arret-travail</c> document, declared once (L11).
///
/// <para><b>Why a constants class and not literals in three files.</b> The editor writes these keys, the
/// validation reads them and the renderer stamps from them — three components in two projects — and the bulletin
/// before it demonstrated exactly what a retyped literal costs: a key spelled differently in one place degrades
/// <i>silently</i>, because a renderer skips a blank field and validation cannot fail a value it never looked at.
/// The result is a form that reports success and prints an empty box.</para>
///
/// <para>⚠️ The three <c>Trauma*</c> values are <b>stored values</b>, not display labels — the renderer's
/// <c>switch</c> matches them to decide which box to tick, so they are the same kind of load-bearing string as
/// <c>CnamInfo</c>'s régime and lien constants. Never re-case or translate them; the French label belongs in the
/// browser.</para>
/// </summary>
public static class ArretTravailKeys
{
    // Patient identity — the « À remplir par l'assuré social » half, prefilled from the fiche.
    public const string IdentifiantUnique = "identifiantUnique";
    public const string PatientFirstName = "patientFirstName";
    public const string PatientLastName = "patientLastName";
    public const string PatientDateOfBirth = "patientDateOfBirth";
    public const string PatientAddress = "patientAddress";
    public const string PostalCode = "postalCode";
    public const string PatientPhone = "patientPhone";

    // Practitioner identity — the certificate's own header.
    public const string DoctorName = "doctorName";
    public const string DoctorQuality = "doctorQuality";
    public const string City = "city";
    public const string DoctorCodeConventionnel = "doctorCodeConventionnel";
    public const string DoctorOrdreNumber = "doctorOrdreNumber";

    // The arrêt itself.
    public const string Days = "days";
    public const string FromDate = "fromDate";
    public const string OutingsFrom = "outingsFrom";
    public const string OutingsTo = "outingsTo";
    public const string TraumaCause = "traumaCause";
    public const string Hospitalised = "hospitalised";

    // The « ..........le,.......... » line above the practitioner's stamp.
    public const string SignPlace = "signPlace";
    public const string SignDate = "signDate";

    // The motif is captured, kept and deliberately **not** printed — see ArretTravailValidation.
    public const string Motif = "motif";

    /// <summary>The three mutually-exclusive traumatisme causes the form offers. Stored values.</summary>
    public const string TraumaVoiePublique = "voie-publique";

    /// <inheritdoc cref="TraumaVoiePublique"/>
    public const string TraumaDomestique = "domestique";

    /// <inheritdoc cref="TraumaVoiePublique"/>
    public const string TraumaViolence = "violence";

    /// <summary>Every accepted <see cref="TraumaCause"/> value. A value outside it is refused at the write.</summary>
    public static readonly IReadOnlyList<string> AllowedTraumaCauses =
        new[] { TraumaVoiePublique, TraumaDomestique, TraumaViolence };

    /// <summary>
    /// The longest arrêt this form will accept, in days. A cap exists because the field is free text and a mis-keyed
    /// « 300 » is an arrêt of ten months on a document that entitles the patient to an indemnity — the kind of
    /// mistake nobody re-reads on paper. 180 days is well past any dental indication.
    /// </summary>
    public const int MaxDays = 180;
}
