using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.DocumentEmails;

/// <summary>
/// The single <see cref="DocumentEmail"/> → <see cref="DocumentEmailDto"/> mapping, shared by the queue command
/// and the history query so the row the practitioner sees after clicking « Envoyer » and the row they see in the
/// history are the same shape.
/// </summary>
public static class DocumentEmailMappingExtensions
{
    public static DocumentEmailDto ToDto(this DocumentEmail email) => new()
    {
        Id = email.Id,
        DocumentKind = email.DocumentKind,
        DocumentId = email.DocumentId,
        RecipientEmail = email.RecipientEmail,
        Subject = email.Subject,
        // The enum name on the wire, French at display time — the standing convention for a closed set.
        Status = email.Status.ToString(),
        Attempts = email.Attempts,
        QueuedAt = email.QueuedAt,
        SentAt = email.SentAt,
        FailureReason = email.FailureReason,
        AttachmentFileName = email.AttachmentFileName
    };
}
