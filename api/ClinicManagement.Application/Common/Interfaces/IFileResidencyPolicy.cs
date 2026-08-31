using ClinicManagement.Application.Common.Files;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Whether this deployment keeps large files in the cabinet's own coffre, and where a given file belongs.
///
/// <para><b>⚠️ This seam is structurally required, not stylistic.</b> <c>DeploymentProfile</c> lives in
/// <b>Infrastructure</b> and <c>ClinicManagement.Application.csproj</c> references <b>Domain alone</b>, so no
/// Application type can name it — the same reason <see cref="ISubscriptionPolicy"/> and
/// <see cref="IOsPushAvailability"/> exist.</para>
///
/// <para>⚠️ <b>The answer is derived from the deployment's kind and from nothing an operator can set.</b> A
/// configuration key able to turn the coffre off would silently start routing a cabinet's CBCT studies back into
/// a hosted store sized for none of them — the <c>httpsConfigured</c> trap, one layer up.</para>
/// </summary>
public interface IFileResidencyPolicy
{
    /// <summary>
    /// Does a coffre exist to send files to? True on the hosted multi-tenant deployment only. Where the clinic's
    /// own machine already holds the blobs there is nothing for this feature to move, so it is <b>absent</b> —
    /// every format reads as always-hosted and the registration door is not published at all.
    /// </summary>
    bool VaultAvailable { get; }

    /// <summary>
    /// Where a file of this format and this size belongs. Always <see cref="FileResidency.Hosted"/> where
    /// <see cref="VaultAvailable"/> is false, so a caller never has to ask both questions.
    /// </summary>
    FileResidency Decide(FileTypeEntry entry, long sizeBytes);
}
