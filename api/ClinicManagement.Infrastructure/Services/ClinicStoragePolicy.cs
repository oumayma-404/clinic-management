using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IClinicStoragePolicy"/> over the deployment profile, beside <see cref="FileResidencyPolicy"/>.
///
/// <para>⚠️ <b>Whether there is a ceiling is derived; how big it is, is configured.</b> The first is a fact about
/// who owns the disk and no operator should be able to turn it off — the same reasoning that keeps the coffre's
/// availability out of configuration. The second is a fact about the hardware that was actually bought, which
/// nothing in the code can know.</para>
/// </summary>
public sealed class ClinicStoragePolicy : IClinicStoragePolicy
{
    /// <summary>
    /// The per-cabinet ceiling where the operator has set none.
    ///
    /// <para>⚠️ <b>Ten gigabytes is chosen against a measured disk, not picked for roundness.</b> The live VPS is
    /// 96 Go with 78 Go free, and since <c>large-file-transfer</c> Part 3 the nightly backup costs about one copy
    /// of the object store rather than fifteen — so a cabinet at this ceiling occupies roughly 20 Go of the disk
    /// all told. At the 150 Mo per-file line that is about seventy full studies, which is years for an ordinary
    /// practice.</para>
    ///
    /// <para>⚠️ <b>It is a ceiling per cabinet, NOT a reservation</b>, and the sum is the operator's to watch:
    /// eight cabinets at this default can promise more than that disk holds. Nothing here can check that — the
    /// deployment does not know how many cabinets it will sell — which is exactly why the number is configurable
    /// and why <c>deploy/README.md</c> states the arithmetic beside it.</para>
    /// </summary>
    public const long DefaultQuotaBytes = 10L * 1024 * 1024 * 1024;

    private readonly DeploymentProfile _profile;
    private readonly long _quotaBytes;

    public ClinicStoragePolicy(DeploymentProfile profile, IConfiguration configuration)
    {
        _profile = profile;

        // A malformed or absurd value falls back to the default rather than failing startup: a typo in one
        // setting must not take a deployment off the air, and the default is safe in the direction that matters.
        //
        // ⚠️ Read as a STRING and parsed here. `GetValue<long?>` looks like it does this and does not — it
        // **throws** on a value that is not a number, so `Deployment:StorageQuotaPerClinicBytes=10Go` would take
        // the whole deployment down at startup with a `FormatException` from a type converter, which is the
        // exact failure the fall-back above exists to prevent. Caught by the test that asserts the fall-back.
        var configured = configuration["Deployment:StorageQuotaPerClinicBytes"];
        _quotaBytes = long.TryParse(configured, out var parsed) && parsed > 0 ? parsed : DefaultQuotaBytes;
    }

    public bool Enforced => !_profile.UsesDiskStorage;

    public long QuotaBytes => Enforced ? _quotaBytes : 0;
}
