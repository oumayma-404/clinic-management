using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One outbound email carrying a generated document (ordonnance, lettre de liaison, certificat, bulletin CNAM,
/// note d'honoraires, avoir, devis, reçu) to a recipient. Queued by the practitioner and dispatched by the
/// minutely <c>DocumentEmailJob</c>, so an offline LAN install queues the send instead of failing it.
/// <para>
/// ⚠️ Deliberately <b>not</b> a row on the <see cref="Notification"/> outbox, despite that table already having
/// a dormant <see cref="NotificationType.Email"/>: its rows carry appointment/recall semantics the reminder
/// dispatcher branches on (re-checking the appointment is still active, <c>ClearRecallSnooze</c> on terminal
/// failure) and a reminder-retention purge. None of that applies to a document, and teaching one dispatcher
/// two meanings is how both get subtly wrong. What is reused is the <i>pattern</i> — connectivity gate,
/// per-row commit, bounded retry, batch cap — and the per-clinic settings + secret-protection infrastructure.
/// </para>
/// <para>
/// The rendered PDF is <b>not</b> held here: it is stored through <c>IFileStorage</c> at queue time and the row
/// keeps only <see cref="AttachmentStorageKey"/>. Rendering happens in the request rather than in the job
/// because every PDF renderer resolves the clinic from the caller's token, which a background job does not
/// have — and doing it up-front means an unrenderable document is refused at the click instead of failing
/// silently a minute later. The blob is deleted once the row reaches a terminal state.
/// </para>
/// </summary>
public class DocumentEmail : Entity<Guid>
{
    // The closed set of sendable document kinds, declared here (the pattern User.AssignableRoles follows) so
    // the entity — not just a handler — is the authority on what may be queued. Values are the wire tokens.
    public const string KindMedicalDocument = "medical-document";
    public const string KindInvoice = "invoice";
    public const string KindCreditNote = "credit-note";
    public const string KindTreatmentPlan = "treatment-plan";
    public const string KindInvoicePaymentReceipt = "invoice-payment-receipt";
    public const string KindInstallmentPaymentReceipt = "installment-payment-receipt";

    public static readonly IReadOnlyList<string> AllowedKinds = new[]
    {
        KindMedicalDocument,
        KindInvoice,
        KindCreditNote,
        KindTreatmentPlan,
        KindInvoicePaymentReceipt,
        KindInstallmentPaymentReceipt
    };

    public Guid ClinicId { get; private set; }

    /// <summary>Which kind of document this email carries — one of <see cref="AllowedKinds"/>.</summary>
    public string DocumentKind { get; private set; } = string.Empty;

    /// <summary>The aggregate the document belongs to (invoice / plan / medical document id).</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>
    /// Extra render keys for the two receipt kinds — a receipt is identified by its payment, not by its parent
    /// alone, so the parent id on its own cannot name the document that was sent.
    /// </summary>
    public Guid? InstallmentId { get; private set; }
    public Guid? PaymentId { get; private set; }

    public string RecipientEmail { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    public string AttachmentStorageKey { get; private set; } = string.Empty;
    public string AttachmentFileName { get; private set; } = string.Empty;

    public DocumentEmailStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime QueuedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>Who asked for the send (the <see cref="User"/> id — a string key, like every user reference).</summary>
    public string? RequestedByUserId { get; private set; }

    private DocumentEmail() { } // For EF Core

    public DocumentEmail(
        Guid clinicId,
        string documentKind,
        Guid documentId,
        string recipientEmail,
        string subject,
        string body,
        string attachmentStorageKey,
        string attachmentFileName,
        Guid? installmentId = null,
        Guid? paymentId = null,
        string? requestedByUserId = null)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("Le cabinet est obligatoire.", nameof(clinicId));
        }

        var kind = NormalizeKind(documentKind)
            ?? throw new ArgumentException("Type de document non pris en charge pour l'envoi par email.", nameof(documentKind));

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Le document est obligatoire.", nameof(documentId));
        }

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            throw new ArgumentException("L'adresse email du destinataire est obligatoire.", nameof(recipientEmail));
        }

        if (string.IsNullOrWhiteSpace(attachmentStorageKey))
        {
            throw new ArgumentException("La pièce jointe est obligatoire.", nameof(attachmentStorageKey));
        }

        Id = Guid.NewGuid();
        ClinicId = clinicId;
        DocumentKind = kind;
        DocumentId = documentId;
        InstallmentId = installmentId;
        PaymentId = paymentId;
        RecipientEmail = recipientEmail.Trim();
        Subject = subject?.Trim() ?? string.Empty;
        Body = body?.Trim() ?? string.Empty;
        AttachmentStorageKey = attachmentStorageKey;
        AttachmentFileName = string.IsNullOrWhiteSpace(attachmentFileName) ? "document.pdf" : attachmentFileName.Trim();
        Status = DocumentEmailStatus.Queued;
        QueuedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The canonical spelling of a document kind, or <c>null</c> when it is not one this feature can send.
    /// Case-insensitive so a client that upper-cases a token is not silently refused.
    /// </summary>
    public static string? NormalizeKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        foreach (var kind in AllowedKinds)
        {
            if (string.Equals(kind, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }

    public void MarkAsSent()
    {
        Status = DocumentEmailStatus.Sent;
        SentAt = DateTime.UtcNow;
        Attempts++;
        FailureReason = null;
    }

    /// <summary>
    /// Records a transient send failure: the row stays <see cref="DocumentEmailStatus.Queued"/> so a later tick
    /// retries it, crossing to <see cref="DocumentEmailStatus.Failed"/> only once the attempt count reaches
    /// <paramref name="maxAttempts"/>. Mirrors <c>Notification.RecordFailedAttempt</c>.
    /// </summary>
    public void RecordFailedAttempt(string? reason, int maxAttempts)
    {
        Attempts++;
        FailureReason = reason;
        if (Attempts >= maxAttempts)
        {
            Status = DocumentEmailStatus.Failed;
        }
    }

    /// <summary>
    /// A failure there is no point retrying (the document no longer exists, its blob is gone). Terminal
    /// immediately — retrying a missing document forever would keep the row queued for good.
    /// </summary>
    public void MarkAsFailed(string? reason)
    {
        Attempts++;
        Status = DocumentEmailStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>
    /// Releases the stored attachment once the row is terminal — the blob exists only to survive the wait
    /// between queueing and sending. Idempotent: clearing an already-cleared key is a no-op.
    /// </summary>
    public void ClearAttachment()
    {
        AttachmentStorageKey = string.Empty;
    }
}
