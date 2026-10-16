using ClinicManagement.Application.Features.Relay;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// « May this PC's copy be thrown away? » — the one answer for every door that can (review finding 9): « Effacer la
/// copie », <c>pair-relay --replace</c> (every reinstall), and <c>uninstall-relay</c>, before it even tells the cloud.
/// ⚠️ A stopped copy is kept whatever stopped it: after a cloud restore, or a cloud that no longer knows this PC, it may
/// be the only place the newer records exist.
/// </summary>
public static class RelayCopyKeep
{
    /// <returns>Null when the copy may go; else the refusal and its code.</returns>
    public static (string Error, string Code)? Refusal(RelayFollowerState state, bool holdsUnreturnedWork)
    {
        if (holdsUnreturnedWork)
        {
            return (RelayRefusals.CutWorkKept, RelayRefusals.CutWorkKeptCode);
        }

        return state.StoppedReason is not null ? (RelayRefusals.NewerCopyKept, RelayRefusals.NewerCopyKeptCode) : null;
    }
}
