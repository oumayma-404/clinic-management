namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>Whether a colleague has the app open right now (staff-chat AC-1.4) — « connecté », never « disponible ».</summary>
public interface IStaffPresence
{
    bool IsOnline(Guid clinicId, string userId);
}
