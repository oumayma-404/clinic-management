using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// « L'encaissement libère le fauteuil » (`salle-attente` S6): money taken for a visit that is still « Au fauteuil »
/// ends that visit, in the same save as the money — the payment proves the patient has left the chair.
///
/// <para><b>Why an interceptor and not a call in each payment command.</b> Money reaches the database through at
/// least six writers — a note's payment, the fiche's « Encaisser », a collection on a treatment, a desk payment on an
/// échéance, a corrected note's carried payments, the devis→facture bridge — and this repository's dominant defect is
/// a correct rule wired to one of them. A new payment rows's insertion is the one fact they all share, so this
/// watches the insertion and no writer can forget it.</para>
///
/// <para>⚠️ <b>Registered before the audit interceptor</b>, so the appointment it closes is in the same audit row set
/// and the PC de secours change capture as the money; and before <see cref="AutomaticWriteInterceptor"/>, which must
/// see the appointment as a person's write. It never throws: a failure here costs the board one card that stays « Au
/// fauteuil » until somebody presses « Terminer », never the payment.</para>
/// </summary>
public class WaitingRoomPaymentInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<WaitingRoomPaymentInterceptor>? _logger;

    public WaitingRoomPaymentInterceptor(ILogger<WaitingRoomPaymentInterceptor>? logger = null)
    {
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ReleaseAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await ReleaseAsync(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task ReleaseAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        try
        {
            var paidInvoiceIds = new HashSet<Guid>();
            var paidRecordIds = new HashSet<Guid>();

            foreach (var entry in context.ChangeTracker.Entries<Payment>())
            {
                if (entry.State == EntityState.Added && !entry.Entity.IsVoided && entry.Entity.Amount > 0)
                {
                    paidInvoiceIds.Add(entry.Entity.InvoiceId);
                    if (entry.Entity.DentalRecordId is { } record)
                    {
                        paidRecordIds.Add(record);
                    }
                }
            }

            foreach (var entry in context.ChangeTracker.Entries<InstallmentPayment>())
            {
                if (entry.State == EntityState.Added && !entry.Entity.IsVoided && entry.Entity.Amount > 0
                    && entry.Entity.DentalRecordId is { } record)
                {
                    paidRecordIds.Add(record);
                }
            }

            if (paidInvoiceIds.Count == 0 && paidRecordIds.Count == 0)
            {
                return;
            }

            var visitIds = new HashSet<Guid>();

            if (paidInvoiceIds.Count > 0)
            {
                // The tracked invoice when the writer loaded it (every writer does), the database otherwise.
                foreach (var invoice in context.ChangeTracker.Entries<Invoice>()
                             .Where(e => paidInvoiceIds.Contains(e.Entity.Id))
                             .Select(e => e.Entity))
                {
                    paidInvoiceIds.Remove(invoice.Id);
                    AddLinks(invoice.AppointmentId, invoice.DentalRecordId, visitIds, paidRecordIds);
                }

                if (paidInvoiceIds.Count > 0)
                {
                    var links = await context.Set<Invoice>()
                        .Where(i => paidInvoiceIds.Contains(i.Id))
                        .Select(i => new { i.AppointmentId, i.DentalRecordId })
                        .ToListAsync(cancellationToken);
                    foreach (var link in links)
                    {
                        AddLinks(link.AppointmentId, link.DentalRecordId, visitIds, paidRecordIds);
                    }
                }
            }

            if (paidRecordIds.Count > 0)
            {
                var tracked = context.ChangeTracker.Entries<DentalRecord>()
                    .Where(e => paidRecordIds.Contains(e.Entity.Id))
                    .Select(e => e.Entity)
                    .ToList();
                foreach (var record in tracked)
                {
                    paidRecordIds.Remove(record.Id);
                    if (record.AppointmentId is { } visit)
                    {
                        visitIds.Add(visit);
                    }
                }

                if (paidRecordIds.Count > 0)
                {
                    var visits = await context.Set<DentalRecord>()
                        .Where(r => paidRecordIds.Contains(r.Id) && r.AppointmentId != null)
                        .Select(r => r.AppointmentId!.Value)
                        .ToListAsync(cancellationToken);
                    visitIds.UnionWith(visits);
                }
            }

            if (visitIds.Count == 0)
            {
                return;
            }

            // Only a visit somebody called in can be in the chair, so the read is narrow by construction.
            var seated = await context.Set<Appointment>()
                .Where(a => visitIds.Contains(a.Id) && a.CalledInAtUtc != null && a.LeftChairAtUtc == null)
                .ToListAsync(cancellationToken);

            var nowUtc = DateTime.UtcNow;
            foreach (var appointment in seated)
            {
                appointment.EndChairIfOpen(nowUtc);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "A payment was saved but the salle d'attente could not release its chair");
        }
    }

    private static void AddLinks(Guid? appointmentId, Guid? recordId, ISet<Guid> visitIds, ISet<Guid> recordIds)
    {
        if (appointmentId is { } visit)
        {
            visitIds.Add(visit);
        }
        else if (recordId is { } record)
        {
            recordIds.Add(record);
        }
    }
}
