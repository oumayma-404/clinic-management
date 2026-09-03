namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Whether this deployment puts a ceiling on how much a single cabinet may store, and where that ceiling sits
/// (<c>large-file-transfer</c> Part 4).
///
/// <para><b>⚠️ This seam is structurally required, not stylistic.</b> <c>DeploymentProfile</c> lives in
/// <b>Infrastructure</b> and <c>ClinicManagement.Application.csproj</c> references <b>Domain alone</b>, so no
/// Application type can name it — the same reason <see cref="IFileResidencyPolicy"/> and
/// <see cref="ISubscriptionPolicy"/> exist.</para>
///
/// <para>⚠️ <b>Enforced is derived from the deployment's kind, exactly as the coffre's availability is</b>, and
/// asks the same question from the other side: <c>UsesDiskStorage</c> means the cabinet's own machine holds the
/// bytes, and metering somebody's own disk back to them is a limit this product has no standing to impose. The
/// hosted multi-tenant box is where one practice's uploads are every other practice's outage.</para>
///
/// <para>⚠️ The <b>size</b> of the ceiling, unlike its existence, IS operator-configurable — it is a fact about
/// the disk that was bought, which no code can know. See the implementation for the default and for the sum an
/// operator has to keep in view.</para>
/// </summary>
public interface IClinicStoragePolicy
{
    /// <summary>True only where the vendor holds the bytes.</summary>
    bool Enforced { get; }

    /// <summary>The per-cabinet ceiling in bytes. Meaningless, and 0, when <see cref="Enforced"/> is false.</summary>
    long QuotaBytes { get; }
}
