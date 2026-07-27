using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Orchestrates one El Fatoora dispatch attempt for a queued invoice (FR-1→FR-5): TEIF → sign → store the
/// signed XML → submit to TTN (client chosen by the clinic's environment) → persist the outcome (validated
/// with QR cachet, permanent rejection, or a bounded retry with backoff). Best-effort and self-committing —
/// it records the result on the invoice and never throws, so the core invoice is never corrupted (a call
/// from a command or the outbox job is always safe).
/// </summary>
public class EInvoiceService : IEInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITeifXmlGenerator _teifXmlGenerator;
    private readonly IEInvoiceSigner _signer;
    private readonly IReadOnlyDictionary<string, ITtnClient> _ttnClients;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EInvoiceService> _logger;

    public EInvoiceService(
        IInvoiceRepository invoiceRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        ITeifXmlGenerator teifXmlGenerator,
        IEInvoiceSigner signer,
        IEnumerable<ITtnClient> ttnClients,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<EInvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _teifXmlGenerator = teifXmlGenerator;
        _signer = signer;
        _ttnClients = ttnClients.ToDictionary(c => c.Environment, StringComparer.OrdinalIgnoreCase);
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        // Best-effort + self-committing: NOTHING below may throw to the caller (called inline from a command
        // and from the outbox job). Every path — the repo loads, the dispatch, and the persist tail — is
        // guarded so a failure only leaves the invoice Queued for a later retry, never corrupts it.
        var maxAttempts = TtnConfig.MaxAttempts(_configuration);
        Invoice? invoice = null;

        try
        {
            invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken);
            if (invoice == null)
            {
                return;
            }

            // Only queued invoices are dispatched; anything else is already terminal or mid-flight.
            if (invoice.EInvoiceStatus != EInvoiceStatus.Queued)
            {
                return;
            }

            var clinic = await _clinicRepository.GetByIdAsync(invoice.ClinicId, cancellationToken);
            var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);

            if (clinic == null)
            {
                RecordTransientFailure(invoice, "Cabinet introuvable.", maxAttempts);
            }
            else
            {
                await DispatchAsync(invoice, clinic, patient, maxAttempts, cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            // Operator-recoverable (e.g. missing certificate): keep it queued so a retry works once fixed.
            _logger.LogWarning(ex, "El Fatoora dispatch of invoice {InvoiceId} could not proceed: {Message}", invoiceId, ex.Message);
            if (invoice != null)
            {
                RecordTransientFailure(invoice, ex.Message, maxAttempts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error dispatching invoice {InvoiceId} to El Fatoora", invoiceId);
            if (invoice != null)
            {
                RecordTransientFailure(invoice, "Erreur lors de l'envoi à El Fatoora.", maxAttempts);
            }
        }

        if (invoice == null)
        {
            return;
        }

        // Persist the recorded outcome — a save failure must NOT propagate (the invoice stays Queued and a
        // later outbox tick retries). Swallow-and-log to honor the never-throws contract.
        try
        {
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            // A concurrent edit here is the one case that must NOT be swallowed as "retry later". The TTN
            // exchange already happened: the invoice is registered, and its identifier and QR payload exist
            // only in this in-memory instance. Dropping them leaves the row Queued, so the next outbox tick
            // re-submits an invoice TTN has already accepted — a duplicate declaration.
            //
            // So reload the row the peer wrote and re-apply the e-invoicing outcome on top of it. Only the
            // e-invoice fields are re-applied; whatever the peer changed (a payment, a cancellation motif)
            // is kept, because it was written from a view of the invoice this dispatch never had.
            _logger.LogWarning(
                "Concurrent edit while persisting the El Fatoora outcome for invoice {InvoiceId}; reloading and re-applying.",
                invoiceId);
            await ReapplyEInvoiceOutcomeAsync(invoice, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist El Fatoora dispatch outcome for invoice {InvoiceId}", invoiceId);
        }
    }

    /// <summary>
    /// Re-apply the e-invoicing outcome of <paramref name="dispatched"/> onto a freshly-loaded invoice, after a
    /// peer's concurrent write. Best-effort like everything else here: if the second attempt also fails, the
    /// state is logged in full so an operator can reconcile against TTN by hand — which is strictly better
    /// than a silent duplicate submission.
    /// </summary>
    private async Task ReapplyEInvoiceOutcomeAsync(Invoice dispatched, CancellationToken cancellationToken)
    {
        try
        {
            var fresh = await _invoiceRepository.GetByIdAsync(dispatched.Id, cancellationToken);
            if (fresh == null)
            {
                _logger.LogError(
                    "Invoice {InvoiceId} vanished while re-applying its El Fatoora outcome (TTN id {TtnId}).",
                    dispatched.Id, dispatched.TtnIdentifier);
                return;
            }

            fresh.CopyEInvoiceStateFrom(dispatched);
            await _invoiceRepository.UpdateAsync(fresh, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not re-apply the El Fatoora outcome for invoice {InvoiceId}. TTN id {TtnId}, status {Status} — "
                + "reconcile manually before the outbox retries.",
                dispatched.Id, dispatched.TtnIdentifier, dispatched.EInvoiceStatus);
        }
    }

    private async Task DispatchAsync(Invoice invoice, Clinic clinic, Patient? patient, int maxAttempts, CancellationToken cancellationToken)
    {
        var input = BuildInput(invoice, clinic, patient);
        var teifXml = _teifXmlGenerator.Generate(input);
        var signed = _signer.Sign(teifXml);

        var signedKey = await StoreArtifactAsync(clinic.Id, invoice, "signed.xml", signed.SignedXml, cancellationToken);
        invoice.MarkEInvoiceSigned(signedKey);

        var client = ResolveClient(clinic.TtnEnvironment);
        var result = await client.SubmitAsync(signed.SignedXml, input.InvoiceNumber, cancellationToken);

        switch (result.Outcome)
        {
            case TtnSubmissionOutcome.Validated:
                var receiptKey = await StoreReceiptAsync(clinic.Id, invoice, result.ReceiptContent, cancellationToken);
                var qrPayload = BuildQrPayload(invoice, clinic, result.TtnIdentifier!, signed.SignedXml);
                invoice.MarkEInvoiceValidated(result.TtnIdentifier!, qrPayload, receiptKey);
                _logger.LogInformation("Invoice {InvoiceId} validated by El Fatoora as {TtnId}", invoice.Id, result.TtnIdentifier);
                break;

            case TtnSubmissionOutcome.Rejected:
                var rejectionReceiptKey = await StoreReceiptAsync(clinic.Id, invoice, result.ReceiptContent, cancellationToken);
                invoice.MarkEInvoiceRejected(result.Error ?? "Rejetée par El Fatoora.", rejectionReceiptKey);
                _logger.LogWarning("Invoice {InvoiceId} rejected by El Fatoora: {Error}", invoice.Id, result.Error);
                break;

            case TtnSubmissionOutcome.TransientFailure:
            default:
                RecordTransientFailure(invoice, result.Error ?? "Échec temporaire de l'envoi.", maxAttempts);
                break;
        }
    }

    private ITtnClient ResolveClient(string environment)
    {
        if (_ttnClients.TryGetValue(environment, out var client))
        {
            return client;
        }

        // Fall back to the sandbox client — never send to production by accident on a misconfigured environment.
        return _ttnClients[Clinic.TtnEnvironmentSandbox];
    }

    private void RecordTransientFailure(Invoice invoice, string error, int maxAttempts)
    {
        var backoffBase = TtnConfig.BackoffBaseSeconds(_configuration);
        var nextAttempt = DateTime.UtcNow.AddSeconds(backoffBase * (invoice.EInvoiceAttemptCount + 1));
        invoice.RecordEInvoiceFailure(error, maxAttempts, nextAttempt);
    }

    private async Task<string> StoreArtifactAsync(Guid clinicId, Invoice invoice, string suffix, string content, CancellationToken cancellationToken)
    {
        var path = $"{clinicId}/e-invoices/{invoice.Id}-{suffix}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await _fileStorage.UploadAsync(stream, "application/xml", path, cancellationToken);
    }

    private async Task<string?> StoreReceiptAsync(Guid clinicId, Invoice invoice, string? receiptContent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(receiptContent))
        {
            return null;
        }
        return await StoreArtifactAsync(clinicId, invoice, "receipt.xml", receiptContent, cancellationToken);
    }

    private static TeifInvoiceInput BuildInput(Invoice invoice, Clinic clinic, Patient? patient) => new()
    {
        InvoiceNumber = invoice.Number ?? invoice.Id.ToString(),
        IssueDate = invoice.IssueDate ?? invoice.CreatedAt,
        SellerName = clinic.Name,
        SellerAddress = clinic.Address,
        SellerMatriculeFiscal = clinic.MatriculeFiscal,
        BuyerName = patient?.GetFullName() ?? "Consommateur final",
        BuyerNationalId = null,
        BuyerMatriculeFiscal = null,
        VatApplicable = invoice.VatApplicable,
        VatRate = invoice.VatRate,
        TotalHt = invoice.TotalHt,
        TotalVat = invoice.TotalVat,
        StampDutyAmount = invoice.StampDutyAmount,
        TotalTtc = invoice.TotalTtc,
        Lines = invoice.Lines
            .Select(l => new TeifInvoiceLineInput
            {
                Designation = l.Designation,
                Quantity = l.Quantity,
                UnitPriceHt = l.UnitPriceHt,
                LineTotalHt = l.LineTotalHt
            })
            .ToList()
    };

    // QR « cachet électronique visible »: TTN id, seller MF, validation timestamp, total TTC, control hash.
    private static string BuildQrPayload(Invoice invoice, Clinic clinic, string ttnId, string signedXml)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signedXml)), 0, 12);
        var parts = new[]
        {
            $"ttn={ttnId}",
            $"mf={clinic.MatriculeFiscal ?? string.Empty}",
            $"num={invoice.Number ?? string.Empty}",
            $"date={DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}",
            $"ttc={invoice.TotalTtc.ToString("0.000", CultureInfo.InvariantCulture)}",
            $"h={hash}"
        };
        return string.Join(";", parts);
    }
}
