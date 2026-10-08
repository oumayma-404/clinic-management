namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// « May this cabinet's rows be written here, now? » (<c>clinic-pc-copy</c> D15) — asked by every path with no request
/// behind it (recurring jobs, startup backfills) <b>before</b> it acts, so a cabinet whose saves belong to its PC de
/// secours is skipped instead of failing the whole run. The change capture's net enforces the same answer at the save;
/// this is what keeps a job from reaching it — and, for the reminder outbox, from sending a message it then cannot
/// record as sent.
/// </summary>
public interface IClinicWriteFence
{
    /// <summary>True on the cloud while the cabinet's PC may hold its saves, and on a PC de secours that does not.</summary>
    Task<bool> RefusesAsync(Guid clinicId, CancellationToken cancellationToken = default);
}
