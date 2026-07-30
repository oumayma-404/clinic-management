using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Daily scan that keeps every clinic's approaching-expiry stock alerts in step with the shelf (AC-P4.6).
///
/// <b>Why a job and not only a write hook.</b> Low stock is crossed by a write — someone consumes the last
/// unit — so <see cref="INotificationGenerator.LowStockAsync"/> can fire inline from the command that caused
/// it. An expiry is crossed by the <i>passage of time</i>: a box nobody touches for six months is exactly the
/// case the alert exists for, and no write happens on the day it enters the lead window. A write-triggered-only
/// implementation would therefore be a notification that never fires for its main case — the class of
/// silently-does-nothing behaviour this whole feature exists to remove. <c>RestockStockItemCommand</c> still
/// calls the generator inline so a delivery that arrives *already* inside the window is flagged at once
/// instead of up to a day later; this job covers everything time does afterwards.
///
/// Runs unconditionally and is <b>not</b> connectivity-gated, unlike <see cref="NotificationJob"/> and
/// <see cref="EInvoiceOutboxJob"/>: the alert it writes is in-app, so it must work on an offline LAN install.
/// Per-item failures are logged and skipped so one bad row cannot stop the scan.
/// </summary>
public class StockExpiryJob
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IStockItemRepository _stockItemRepository;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly ILogger<StockExpiryJob> _logger;

    public StockExpiryJob(
        IClinicRepository clinicRepository,
        IStockItemRepository stockItemRepository,
        INotificationGenerator notificationGenerator,
        ILogger<StockExpiryJob> logger)
    {
        _clinicRepository = clinicRepository;
        _stockItemRepository = stockItemRepository;
        _notificationGenerator = notificationGenerator;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public async Task FlagExpiringStock()
    {
        var now = DateTime.UtcNow;
        var clinics = await _clinicRepository.GetAllAsync();

        foreach (var clinic in clinics)
        {
            try
            {
                await ScanClinicAsync(clinic, now);
            }
            catch (Exception ex)
            {
                // One clinic's failure must not stop the others — the scan is per-clinic independent.
                _logger.LogError(ex, "Approaching-expiry scan failed for clinic {ClinicId}", clinic.Id);
            }
        }
    }

    private async Task ScanClinicAsync(Clinic clinic, DateTime nowUtc)
    {
        var leadDays = clinic.StockExpiryLeadDays;
        if (leadDays <= 0)
        {
            // A non-positive lead window means the clinic has the alert switched off — the same reading
            // StockItemDto applies to `isExpiringSoon`. Do not scan, and do not clear existing alerts:
            // turning the window off should stop new alerts, not silently erase what staff were already told.
            return;
        }

        var items = (await _stockItemRepository.GetByClinicIdAsync(clinic.Id)).Items;

        foreach (var item in items)
        {
            var expiry = item.HasStockExpiringSoon(nowUtc, leadDays) ? item.EarliestRelevantExpiry() : null;

            // `HasStockExpiringSoon` is true only for a lot that still holds stock and carries a date, so a
            // true reading always yields an expiry here — the null-check is what keeps that assumption honest
            // rather than dereferencing on it.
            if (expiry.HasValue)
            {
                await _notificationGenerator.EnsureStockExpiringSoonAsync(
                    clinic.Id, item.Id, item.Name, expiry.Value);
            }
            else
            {
                // No longer expiring soon (consumed, discarded, or the lot's date moved past the window).
                // Clearing is not just tidiness: it is what allows this item's NEXT batch to alert.
                await _notificationGenerator.ClearStockExpiringSoonAsync(clinic.Id, item.Id);
            }
        }
    }
}
