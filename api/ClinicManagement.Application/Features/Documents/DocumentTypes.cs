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
}
