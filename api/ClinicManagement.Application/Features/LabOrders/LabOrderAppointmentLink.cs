using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders;

/// <summary>
/// The single validation of « ce bon appartient-il vraiment à cette séance ? » (AC-23), shared by the create and
/// update paths.
///
/// <para>Shared rather than written twice on purpose: <c>Invoice.AppointmentId</c> spent the product's whole life
/// as a column the create path accepted and nobody validated, and the lesson recorded from it is that a link
/// checked on one door and not the other is the same defect wearing a different hat. Both doors call this.</para>
///
/// <para>It mirrors <c>CreateInvoiceCommand</c>'s pattern exactly — <b>clinic and patient</b>, in that order. The
/// patient half is the one that is easy to omit and the one that matters: a bon silently attached to another
/// patient's visit would show that patient's crown on the wrong file, and the tenant check alone cannot see it
/// because both rows are in the caller's own clinic.</para>
/// </summary>
public static class LabOrderAppointmentLink
{
    /// <summary>
    /// Validates an optional appointment link. A null <paramref name="appointmentId"/> is success — a bon ordered
    /// between visits is ordinary, not an omission.
    /// </summary>
    public static async Task<Result> ValidateAsync(
        IAppointmentRepository appointments,
        Guid? appointmentId,
        Guid clinicId,
        Guid patientId,
        CancellationToken cancellationToken)
    {
        if (!appointmentId.HasValue)
        {
            return Result.Success();
        }

        var appointment = await appointments.GetByIdAsync(appointmentId.Value, cancellationToken);
        if (appointment == null || appointment.ClinicId != clinicId)
        {
            return Result.Failure("Rendez-vous introuvable.");
        }

        if (appointment.PatientId != patientId)
        {
            return Result.Failure(
                "Ce rendez-vous appartient à un autre patient : le bon de prothèse ne peut pas y être rattaché.");
        }

        return Result.Success();
    }
}
