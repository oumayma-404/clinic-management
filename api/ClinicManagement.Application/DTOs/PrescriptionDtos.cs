namespace ClinicManagement.Application.DTOs;

/// <summary>
/// What the fiche de soins prescribed at this séance — the payload that becomes, or updates, the séance's own
/// ordonnance (a <c>MedicalDocument</c> of type <c>prescription</c>).
///
/// <para>
/// ⚠️ <b>Always sent by the client, never omitted.</b> The fiche's act list is replaced wholesale on every save
/// (<c>DentalRecord.SetActs</c>) and this follows the same discipline for the same reason: a field the modal
/// forgets to re-send on a routine re-save is this product's most expensive recurring defect. An empty
/// <see cref="Lines"/> therefore means « nothing prescribed this time », not « leave it alone » — and it still
/// does not delete an ordonnance that already exists, because that paper may be in the patient's hand. See
/// <c>FicheOrdonnanceEmitter</c>.
/// </para>
/// </summary>
public class PrescriptionInput
{
    /// <summary>The prescribed lines, in the order they will print.</summary>
    public List<PrescriptionLineInput> Lines { get; set; } = new();

    /// <summary>
    /// The renouvellement mention, which governs the whole ordonnance and never a single line. Blank prints
    /// nothing; <c>"0"</c> / <c>"non"</c> prints « Ordonnance non renouvelable. » See
    /// <c>PrescriptionContent.RenewalMention</c>.
    /// </summary>
    public string? Renewals { get; set; }

    /// <summary>
    /// The <c>Version</c> of the ordonnance the modal read when it opened, round-tripped so a save cannot
    /// overwrite an edit made through <c>/documents/prescription</c> in the meantime. <b>0 means « not
    /// supplied »</b> and turns the check off, which is the solution-wide rule.
    ///
    /// <para>
    /// ⚠️ <b>This is not belt-and-braces — without it the overwrite is silent, and it was.</b> The obvious
    /// assumption (and the one an earlier comment in <c>FicheOrdonnanceEmitter</c> asserted) is that the
    /// document's own <c>xmin</c> protects it. It cannot: the emitter loads the document <i>inside</i> the
    /// fiche's transaction, so the tracked entity always carries the CURRENT token and its update always
    /// succeeds. The stale copy lives in the browser's section state, where <c>xmin</c> can never see it.
    /// Measured end to end: a colleague's correction to a médicament's name was reverted by the next fiche
    /// save, with a green success toast and no refusal.
    /// </para>
    /// </summary>
    public uint PrescriptionDocumentVersion { get; set; }

    /// <inheritdoc cref="PrescriptionDocumentVersion"/>
    public uint ExamensDocumentVersion { get; set; }
}

/// <summary>
/// One line of the séance's ordonnance. Mirrors the shape already persisted inside a prescription's
/// <c>content.medications</c> array (<c>PrescriptionContent.MedicationEntry</c>) plus
/// <see cref="Kind"/> — so the fiche writes exactly what the document editor writes, and the two can edit each
/// other's work.
/// </summary>
public class PrescriptionLineInput
{
    /// <summary>
    /// <c>medicament</c> or <c>examen</c> — see <c>PrescriptionLineKinds</c>. Absent is read as a médicament.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// The médicament (« Augmentin Comprimé ») or, for an examen, the whole request written as the
    /// practitioner would say it (« Radiographie panoramique dentaire — bilan pré-implantaire »). Printed
    /// verbatim.
    /// </summary>
    public string? Name { get; set; }

    public string? Dosage { get; set; }
    public string? TimesPerDay { get; set; }

    /// <summary>Voie d'administration — free text, because the norms name no closed list.</summary>
    public string? Route { get; set; }

    /// <summary>Quantité à délivrer (boîtes / unités).</summary>
    public string? Quantity { get; set; }

    public string? Duration { get; set; }

    /// <summary>
    /// The catalogue entry this line was picked from, when it was. Absent for a free-text drug — which is a
    /// first-class case, not a fallback.
    /// </summary>
    public Guid? MedicationId { get; set; }

    /// <summary>
    /// The molecules, snapshotted at the moment of selection rather than read through
    /// <see cref="MedicationId"/>: deactivating a catalogue entry must not rewrite an ordonnance already
    /// issued.
    /// </summary>
    public List<string> Dci { get; set; } = new();
}
