namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Pushes an appointment to Google Calendar after its command has committed — fire-and-forget,
/// connectivity-gated, and never throws back to the caller. Extracted so the appointment command handlers
/// don't each duplicate the scope + connectivity-gate + background-task logic (nor act as service locators).
/// Implemented in the Application layer over <c>IServiceScopeFactory</c> (the work outlives the request scope).
/// </summary>
public interface IAppointmentGoogleSyncDispatcher
{
    /// <summary>
    /// <paramref name="clinicId"/> is the appointment's own clinic, and the caller supplies it because the child
    /// scope cannot look it up: <c>Appointment</c> is clinic-filtered, so an unscoped read of it comes back empty
    /// and the sync would give up with « appointment not found » on every push (US-2). Every caller has already
    /// resolved it.
    /// </summary>
    void Dispatch(Guid appointmentId, Guid clinicId);
}
