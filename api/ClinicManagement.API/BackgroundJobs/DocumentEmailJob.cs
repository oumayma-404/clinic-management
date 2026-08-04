using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Connectivity-gated dispatcher for the document-email outbox. Runs minutely: when the server has internet it
/// sends each queued row's stored PDF over that clinic's resolved SMTP settings, then releases the blob. Offline
/// ⇒ it does nothing and leaves rows queued, which is the whole reason the send is an outbox: on an offline LAN
/// install the practitioner's click has to mean "send this when you can", not "fail".
/// <para>
/// Shaped after <see cref="EInvoiceOutboxJob"/> and <c>NotificationJob</c>: batch-bounded, per-row commit (one
/// bad row cannot abort the tick), bounded retry. It does <b>not</b> render anything — the attachment was
/// rendered and stored in the request, because every PDF renderer resolves the clinic from the caller's token
/// and a job has none.
/// </para>
/// </summary>
public class DocumentEmailJob
{
    // Kept in step with the reminder outbox's own cap rather than introducing a third config knob.
    private const int DefaultBatchSize = 20;
    private const int DefaultMaxAttempts = 5;

    private readonly IDocumentEmailRepository _documentEmailRepository;
    private readonly IDocumentEmailSender _sender;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IFileStorage _fileStorage;
    private readonly IInternetProbe _internetProbe;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentEmailJob> _logger;

    public DocumentEmailJob(
        IDocumentEmailRepository documentEmailRepository,
        IDocumentEmailSender sender,
        IReminderSettingsProvider settingsProvider,
        IFileStorage fileStorage,
        IInternetProbe internetProbe,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<DocumentEmailJob> logger)
    {
        _documentEmailRepository = documentEmailRepository;
        _sender = sender;
        _settingsProvider = settingsProvider;
        _fileStorage = fileStorage;
        _internetProbe = internetProbe;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public async Task DispatchQueuedEmails()
    {
        // The server (not a LAN client) is the source of truth for internet egress. Offline ⇒ send nothing and
        // consume no retry, so a week offline does not exhaust every row's attempts.
        if (!await _internetProbe.IsInternetReachableAsync())
        {
            _logger.LogInformation("Skipping document-email dispatch — server has no internet connectivity.");
            return;
        }

        var batchSize = _configuration.GetValue<int?>("Notification:Smtp:DispatchBatchSize") is > 0
            ? _configuration.GetValue<int>("Notification:Smtp:DispatchBatchSize")
            : DefaultBatchSize;
        var maxAttempts = _configuration.GetValue<int?>("Notification:Smtp:MaxAttempts") is > 0
            ? _configuration.GetValue<int>("Notification:Smtp:MaxAttempts")
            : DefaultMaxAttempts;

        var queued = await _documentEmailRepository.GetQueuedAsync(batchSize);

        foreach (var row in queued)
        {
            try
            {
                await DispatchOneAsync(row, maxAttempts);
            }
            catch (Exception ex)
            {
                // Defensive: one row must never abort the batch.
                _logger.LogError(ex, "Unexpected error dispatching document email {DocumentEmailId}", row.Id);
            }
        }
    }

    private async Task DispatchOneAsync(DocumentEmail row, int maxAttempts)
    {
        // The row's own clinic, not the caller's — the job has no clinic in scope, which is what lets one tick
        // drain every cabinet's queue.
        var settings = await _settingsProvider.ResolveAsync(row.ClinicId);
        if (!settings.EmailConfigured)
        {
            // The cabinet's settings were removed after the row was queued. Not a send failure: leave it queued
            // and consume no attempt, the same way an offline tick does — restoring the settings resumes it.
            _logger.LogWarning(
                "Document email {DocumentEmailId} left queued — clinic {ClinicId} has no usable SMTP settings.",
                row.Id, row.ClinicId);
            return;
        }

        byte[] attachment;
        try
        {
            attachment = await ReadAttachmentAsync(row);
        }
        catch (Exception ex)
        {
            // The blob is gone (storage wiped, key lost). Retrying cannot bring it back, so this is terminal —
            // otherwise the row would sit queued forever looking like it is about to be sent.
            _logger.LogError(ex, "Attachment for document email {DocumentEmailId} could not be read; marking failed.", row.Id);
            row.MarkAsFailed("La pièce jointe du document est introuvable.");
            await PersistAsync(row);
            return;
        }

        var result = await _sender.SendAsync(
            new DocumentEmailMessage(row.RecipientEmail, row.Subject, row.Body, attachment, row.AttachmentFileName),
            settings);

        switch (result.Outcome)
        {
            case DocumentEmailSendOutcome.Sent:
                row.MarkAsSent();
                await ReleaseAttachmentAsync(row);
                break;

            case DocumentEmailSendOutcome.NotConfigured:
                // Resolved as sendable a moment ago but the sender disagrees — leave it queued rather than
                // burning attempts against a configuration problem no retry can fix.
                _logger.LogWarning("Document email {DocumentEmailId} left queued — sender reports SMTP not configured.", row.Id);
                return;

            default:
                row.RecordFailedAttempt(result.Error, maxAttempts);
                if (row.Status == Domain.Enums.DocumentEmailStatus.Failed)
                {
                    await ReleaseAttachmentAsync(row);
                }
                break;
        }

        await PersistAsync(row);
    }

    private async Task<byte[]> ReadAttachmentAsync(DocumentEmail row)
    {
        await using var stream = await _fileStorage.DownloadAsync(row.AttachmentStorageKey);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Drops the stored PDF once the row is terminal — it existed only to survive the wait between queueing and
    /// sending, and leaving it behind would accumulate PHI blobs no screen can reach. Best-effort: a storage
    /// failure must not stop the row being recorded as sent.
    /// </summary>
    private async Task ReleaseAttachmentAsync(DocumentEmail row)
    {
        var key = row.AttachmentStorageKey;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        try
        {
            await _fileStorage.DeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete the sent attachment blob for document email {DocumentEmailId}", row.Id);
        }

        row.ClearAttachment();
    }

    // Per-row commit, like the reminder dispatcher: a later row's failure must not roll back an email that was
    // genuinely delivered — a second send is not recoverable.
    private async Task PersistAsync(DocumentEmail row)
    {
        await _documentEmailRepository.UpdateAsync(row);
        await _unitOfWork.SaveChangesAsync();
    }
}
