using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Reads the dashboard's « Argent » section plus the point-in-time créances total. The two are produced together
/// because both need the clinic's billed-plan set, and computing it twice is both wasteful and a chance for the
/// cash and debt sides to de-duplicate differently.
/// </summary>
public interface IDashboardMoneyReader
{
    /// <param name="doctorId">
    /// L9 — narrow « Encaissé » and « Facturé » to one practitioner. ⚠️ Dépenses, Net and Créances stay clinic-wide
    /// even so, and the DTO flags it: an expense has no practitioner, so a narrowed Net would be one dentist's
    /// income minus everybody's costs. Required rather than defaulted, deliberately — a new caller must decide
    /// whether it is asking about the practice or about one practitioner, not inherit an answer.
    /// </param>
    Task<(DashboardMoneyDto Money, DashboardReceivablesDto Receivables)> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, Guid? doctorId, CancellationToken cancellationToken);
}
