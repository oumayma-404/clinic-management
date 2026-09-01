namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// « Take a restorable copy of this cabinet's rows, now » — the net a destructive bulk operation puts under
/// itself before it starts.
///
/// <para><b>Why a seam rather than the static writer called inline.</b> Its one caller today is
/// « Annuler cet import », which deletes patient records in bulk with no vendor in the loop. Calling
/// <c>ClinicRecoveryPointWriter</c> directly from that handler would put a zip build, an object-store upload and
/// two saves on the handler's own dependency list — and, more to the point, would make « does the undo refuse
/// when it cannot take a net? » untestable without a real archive store. The nightly
/// <c>ClinicRecoveryPointJob</c> keeps calling the writer directly: it needs the point <i>row</i> to decide
/// whether to prune, which is a different question from the one asked here.</para>
/// </summary>
public interface IClinicRecoveryPointService
{
    /// <summary>
    /// Take one point for <paramref name="clinicId"/> and report whether it is <b>restorable</b>.
    ///
    /// <para>⚠️ Never throws: a failure is <c>false</c>, and the attempt is recorded either way — so « il essaie
    /// et il échoue » stays visible on « Points de restauration » rather than being swallowed into the caller's
    /// own error. What the caller does with a <c>false</c> is the caller's decision; the undo refuses.</para>
    /// </summary>
    Task<bool> TryTakeAsync(Guid clinicId, CancellationToken cancellationToken = default);
}
