namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// Shared French filename mapping for generated medical documents. Previously this switch was duplicated
/// (and had drifted) between the create and update command handlers — the update copy was missing the
/// <c>bulletin-cnam</c> arm, so a re-saved BS1 was filed under the raw type name instead of
/// <c>bulletin-de-soins-cnam</c> (FR-6.2). Centralising it keeps create and update in lock-step.
/// </summary>
public static class DocumentFileNaming
{
    public static string GetDocumentTypeName(string documentType) =>
        documentType.ToLowerInvariant() switch
        {
            DocumentTypes.Prescription => "ordonnance",
            DocumentTypes.Liaison => "lettre-de-liaison",
            DocumentTypes.Honoraires => "note-d-honoraires",
            DocumentTypes.Certificat => "certificat-medical",
            DocumentTypes.BulletinCnam => "bulletin-de-soins-cnam",
            _ => documentType.ToLowerInvariant()
        };
}
