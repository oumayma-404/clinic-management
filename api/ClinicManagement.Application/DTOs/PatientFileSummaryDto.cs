namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One card of the « Fichiers » directory: a patient, and the state of their own file drawer.
///
/// <para><c>LastUploadedAt</c> is null for a patient whose drawer is empty, and the client says so in words
/// (« Aucun fichier ») rather than rendering a date it does not have. <c>TotalBytes</c> is 0 in that case —
/// <c>FileCount</c> is what distinguishes « rien » from « des fichiers vides », so a nullable size would be a
/// second way to ask the same question.</para>
/// </summary>
public class PatientFileSummaryDto
{
    public Guid PatientId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>The stored number, exactly as it was typed. Null when the patient gave none.</summary>
    public string? PhoneNumber { get; set; }

    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public DateTime? LastUploadedAt { get; set; }
}
