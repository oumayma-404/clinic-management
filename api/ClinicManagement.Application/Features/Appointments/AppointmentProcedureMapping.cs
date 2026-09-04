using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// The one place a séance's acts become DTOs.
/// <para>
/// Shared rather than inlined at each of the four sites that build an <see cref="AppointmentDto"/> (list read,
/// single read, create, update) for the same reason <c>AppointmentInvoiceLinks</c> is: they must agree. In
/// particular they must agree on the **name fallback** — prefer the live catalog entry, fall back to the snapshot
/// taken at booking — or the same visit would name its acts differently on the agenda and in the edit dialog once
/// a procedure is renamed or retired.
/// </para>
/// </summary>
public static class AppointmentProcedureMapping
{
    public static List<AppointmentProcedureDto> ToProcedureDtos(this Appointment appointment) =>
        appointment.Procedures
            .Select(p => new AppointmentProcedureDto
            {
                Id = p.Id,
                ProcedureTypeId = p.ProcedureTypeId,
                // The nav is only loaded on the reads that Include it; the snapshot is what makes this correct
                // everywhere else (and the only thing that still names a retired procedure).
                Name = p.ProcedureType?.Name ?? p.ProcedureName,
                DurationMinutes = p.DurationMinutes,
                ColorHex = p.ProcedureType?.Color.Value ?? p.ColorHex,
                TreatmentPlanItemId = p.TreatmentPlanItemId,
                TreatmentPlanItemStepId = p.TreatmentPlanItemStepId,
                // Never falls back to the catalogue's DefaultCost: null here means « nothing was negotiated », and
                // the client is what decides that a missing agreed price shows the tarif instead.
                AgreedCost = p.AgreedCost,
                SequenceNumber = p.SequenceNumber,
            })
            .ToList();

    /// <summary>
    /// The lead act's display name. Falls back through the séance's first row, so a visit booked with acts always
    /// names one even on a read that did not <c>Include</c> the parent's <c>ProcedureType</c>.
    /// </summary>
    public static string? LeadProcedureName(this Appointment appointment) =>
        appointment.ProcedureType?.Name
        ?? appointment.Procedures.FirstOrDefault()?.ProcedureType?.Name
        ?? appointment.Procedures.FirstOrDefault()?.ProcedureName;
}
