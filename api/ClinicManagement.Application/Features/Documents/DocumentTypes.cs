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
}
