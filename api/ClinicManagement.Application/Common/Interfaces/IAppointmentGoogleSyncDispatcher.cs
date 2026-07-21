namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Pushes an appointment to Google Calendar after its command has committed — fire-and-forget,
/// connectivity-gated, and never throws back to the caller. Extracted so the appointment command handlers
/// don't each duplicate the scope + connectivity-gate + background-task logic (nor act as service locators).
/// Implemented in the Application layer over <c>IServiceScopeFactory</c> (the work outlives the request scope).
/// </summary>
public interface IAppointmentGoogleSyncDispatcher
{
    void Dispatch(Guid appointmentId);
}
