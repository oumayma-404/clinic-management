namespace ClinicManagement.Application.Common.Models;

public class MedicalDocumentPdfData
{
    public string DocumentType { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    
    // Patient Info
    public string PatientName { get; set; } = string.Empty;

    /// <summary>
    /// ⚠️ Holds the patient's <b>formatted date de naissance</b> (dd/MM/yyyy), not an age — it is labelled
    /// « Date de naissance » wherever it renders. Deliberately not renamed: it is a persisted
    /// <c>MedicalDocument</c> column <i>and</i> a field on the body the client posts to
    /// <c>generate-pdf-download</c>, so a rename costs a migration and a wire-contract change for no
    /// behavioural gain.
    /// </summary>
    public string? PatientAge { get; set; }
    public string? PatientId { get; set; }

    /// <summary>
    /// Patient sex — mandatory on a prescription for listes I/II medicines (R.5132-3), which covers most
    /// antibiotics and analgesics a dentist prescribes. Snapshotted from <c>Patient.Gender</c>, printed
    /// verbatim (a historical row may hold a free-text value).
    /// </summary>
    public string? PatientSex { get; set; }

    /// <summary>
    /// Patient weight as entered on <b>this</b> document — never read from the patient record, because a
    /// stored weight goes stale and a stale weight that looks verified is worse than a blank field. Free text:
    /// it is a mention on a document, not an input to a calculation.
    /// </summary>
    public string? PatientWeightKg { get; set; }

    // Clinic Info
    public string ClinicName { get; set; } = string.Empty;
    public string ClinicAddress { get; set; } = string.Empty;
    public string ClinicPhone { get; set; } = string.Empty;

    /// <summary>Cabinet email — part of the prescriber's contact details a prescription must carry.</summary>
    public string? ClinicEmail { get; set; }

    public string DoctorName { get; set; } = string.Empty;
    public string DoctorSpecialty { get; set; } = string.Empty;

    /// <summary>
    /// Which practitioner's cachet + n° d'ordre to resolve — the editor's explicit choice, the id behind
    /// <see cref="DoctorName"/>. <b>Unlike the four snapshot fields below it, this one is NOT stripped from the
    /// client payload</b>, and the distinction is the whole security argument: this is a <em>selector</em>, checked
    /// against the caller's own clinic roster, whereas <see cref="DoctorCachetKey"/> is a storage key the
    /// unauthenticated PDF job dereferences and must therefore only ever come from the server.
    /// </summary>
    public Guid? IssuingDoctorId { get; set; }

    // Snapshotted practitioner/clinic fields (Part C, FR-3.3 / FR-6.1). Populated by both producers — the
    // create command snapshots them into ContentJson so the unauthenticated background PDF job can render
    // the cachet + city without a live doctor/clinic lookup.
    public string? ClinicCity { get; set; }             // "{City}, le …" place line (never a hardcoded "Paris")
    public string? DoctorOrdreNumber { get; set; }      // CNOMDT registration number (snapshot)
    public string? DoctorCachetKey { get; set; }        // IFileStorage key of the practitioner cachet image
    public string? DoctorCachetContentType { get; set; } // persisted MIME type of that image

    // Recipient (for liaison documents)
    public string? RecipientDoctorName { get; set; }
    public string? RecipientDoctorSpecialty { get; set; }
    
    // Document Content (varies by type)
    public Dictionary<string, string> Content { get; set; } = new();
}






