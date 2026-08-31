using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IFileResidencyPolicy"/> over the deployment profile, beside <c>SubscriptionPolicy</c> and
/// <c>OsPushAvailability</c>.
///
/// <para>⚠️ <b>Derived from <c>UsesDiskStorage</c> rather than from a capability of its own.</b> That flag already
/// answers the only question that matters here — does the vendor hold this cabinet's bytes? — so a twenty-second
/// capability beside it would be a second way to ask one thing, and the two could disagree. Where the clinic's own
/// machine is the object store there is nothing to move out of it.</para>
/// </summary>
public sealed class FileResidencyPolicy : IFileResidencyPolicy
{
    private readonly DeploymentProfile _profile;

    public FileResidencyPolicy(DeploymentProfile profile)
    {
        _profile = profile;
    }

    public bool VaultAvailable => !_profile.UsesDiskStorage;

    public FileResidency Decide(FileTypeEntry entry, long sizeBytes) =>
        VaultAvailable ? entry.Residency.Decide(sizeBytes) : FileResidency.Hosted;
}
