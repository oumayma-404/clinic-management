using ClinicManagement.Application.Features.Relay;
using Microsoft.Win32;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>Where a PC de secours writes the hours Windows Update must not restart it in (AC-3.11).</summary>
public interface IRelayUpdateHoursSink
{
    /// <summary>Sets Windows' active hours; false with the reason when it cannot (an ordinary account, another system).</summary>
    bool TryApply(RelayActiveHours hours, out string? error);
}

/// <summary>
/// The Windows Update policy keys (<c>HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate</c>: <c>SetActiveHours</c>,
/// <c>ActiveHoursStart</c>, <c>ActiveHoursEnd</c>) — policy rather than the user setting, so nobody's « heures
/// d'activité » choice in Windows' screens can widen the window past the cabinet's. The service runs as LocalSystem.
/// </summary>
public sealed class WindowsUpdateHours : IRelayUpdateHoursSink
{
    public const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public bool TryApply(RelayActiveHours hours, out string? error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "ce système n'a pas Windows Update.";
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(PolicyKey, writable: true);
            key.SetValue("SetActiveHours", 1, RegistryValueKind.DWord);
            key.SetValue("ActiveHoursStart", hours.Start, RegistryValueKind.DWord);
            key.SetValue("ActiveHoursEnd", hours.End, RegistryValueKind.DWord);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            error = "le service du PC de secours n'a pas le droit de régler Windows Update.";
            return false;
        }
    }
}
