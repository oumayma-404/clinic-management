namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// Canonical document-type discriminators (lowercase, as persisted on <c>MedicalDocument.DocumentType</c>),
/// shared across the create/update command handlers, <see cref="DocumentFileNaming"/> and the PDF renderer,
/// so the type tokens can no longer drift between duplicated string literals.
/// </summary>
public static class DocumentTypes
{
    public const string Prescription = "prescription";
    public const string Liaison = "liaison";
    public const string Certificat = "certificat";
    public const string Honoraires = "honoraires";
    public const string BulletinCnam = "bulletin-cnam";

    /// <summary>
    /// « Certificat médical d'arrêt de travail » — stamped onto the genuine CNAM <b>P 061</b> form (L11).
    /// A token only: the type is otherwise indistinguishable from its siblings, which is what lets the create /
    /// update / naming / render paths each recognise it by comparing to this constant instead of a literal.
    /// </summary>
    public const string ArretTravail = "arret-travail";

    /// <summary>
    /// « Demande d'examens » — the ordonnance that sends a patient for an examen rather than to a pharmacie:
    /// une radiographie panoramique, un bilan sanguin, l'avis d'un confrère.
    ///
    /// <para>
    /// ⚠️ <b>It exists because an examen may not share a sheet with a médicament.</b> The rule is that a
    /// prescriber writes « sur des ordonnances distinctes » the médicaments, the produits et prestations, and
    /// the examens de laboratoire — and three practical facts make it a rule rather than a formality: the
    /// sheets go to different places (la pharmacie, le laboratoire, le centre d'imagerie), an examen
    /// prescription is single-use unless the prescriber says otherwise so one sheet cannot serve two of them,
    /// and CNAM claims a reimbursement per line with the matching prescription attached. Our
    /// <see cref="Prescription"/> document is specifically a médicament form — <c>ordonnance-certificat-norms</c>
    /// built it to R.5132-3 with voie d'administration, quantité and the renouvellement mention — so a
    /// panoramique printed on it is an imaging request on a drug prescription.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>The printed title is « ORDONNANCE », the same as its médicament sibling, and that is deliberate.</b>
    /// That is what this document legally is, and what a laboratoire or a CNAM clerk expects to read at the top
    /// of the page; the body's own opening sentence says what is being prescribed. The distinct label
    /// « Demande d'examens » exists for the app's Documents tab, where the two must be told apart at a glance.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Not creatable from the document gallery, on purpose.</b> A séance prescribes it — the fiche de
    /// soins is the only writer — so <c>web/lib/documents.ts</c> marks the template <c>creatable: false</c> and
    /// the standalone editor never has to render it. Flipping that flag is the whole of « let a dentist write
    /// one without recording a fiche », and it needs the editor's form + preview first.
    /// </para>
    /// </summary>
    public const string Examens = "examens";
}
