namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One document-email send, as the UI reads it under « Envois par email ». Carries no attachment and no
/// storage key — the recipient, the moment and the outcome are what a practitioner needs to know, and a
/// storage key on the wire would be a handle to a stored PHI blob.
/// </summary>
public class DocumentEmailDto
{
    public Guid Id { get; set; }
    public string DocumentKind { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    /// <summary>`Queued` | `Sent` | `Failed` — the enum name, mapped to French at display time.</summary>
    public string Status { get; set; } = string.Empty;

    public int Attempts { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? FailureReason { get; set; }
    public string AttachmentFileName { get; set; } = string.Empty;
}
