using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>The list is shared, so its change reaches the whole cabinet — the only clinic-wide chat signal (US-2).</summary>
internal static class StaffQuickMessageSignal
{
    public static async Task SendAsync(IRealtimeNotifier realtime, Guid clinicId, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await realtime.NotifyStaffChatAsync(clinicId, null, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Quick messages of clinic {ClinicId} saved but the cabinet was not signalled", clinicId);
        }
    }
}
