using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Connectivity-gated dispatcher for the TTN « El Fatoora » outbox (FR-4). Runs minutely: when the server
/// has internet it dispatches each due <c>Queued</c> invoice (attempts due now) via <see cref="IEInvoiceService"/>,
/// which signs, submits and records the outcome (validated / rejected / bounded retry with backoff). Offline
/// ⇒ it does nothing and leaves invoices queued (mirrors the SMS reminder + Google "non synchronisé" model).
/// </summary>
public class EInvoiceOutboxJob
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IEInvoiceService _eInvoiceService;
    private readonly IInternetProbe _internetProbe;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EInvoiceOutboxJob> _logger;

    public EInvoiceOutboxJob(
        IInvoiceRepository invoiceRepository,
        IEInvoiceService eInvoiceService,
        IInternetProbe internetProbe,
        IConfiguration configuration,
        ILogger<EInvoiceOutboxJob> logger)
    {
        _invoiceRepository = invoiceRepository;
        _eInvoiceService = eInvoiceService;
        _internetProbe = internetProbe;
        _configuration = configuration;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public async Task DispatchQueuedInvoices()
    {
        // The server (not a LAN client) is the source of truth for internet egress. Offline ⇒ dispatch
        // nothing and leave invoices Queued; the next tick with connectivity picks them up.
        if (!await _internetProbe.IsInternetReachableAsync())
        {
            _logger.LogInformation("Skipping El Fatoora dispatch — server has no internet connectivity.");
            return;
        }

        var batchSize = TtnConfig.DispatchBatchSize(_configuration);
        var due = await _invoiceRepository.GetDueForElFatooraDispatchAsync(batchSize, DateTime.UtcNow);

        foreach (var invoice in due)
        {
            try
            {
                // Self-committing + best-effort: it records its own outcome and never throws back.
                await _eInvoiceService.ProcessAsync(invoice.Id);
            }
            catch (Exception ex)
            {
                // Defensive: one invoice must never abort the batch.
                _logger.LogError(ex, "Unexpected error dispatching invoice {InvoiceId} to El Fatoora", invoice.Id);
            }
        }
    }
}
