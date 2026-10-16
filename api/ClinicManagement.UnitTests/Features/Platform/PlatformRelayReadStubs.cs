using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The dependency both console reads grew with the « PC de secours » column (<c>clinic-pc-copy</c> AC-9.1): a deployment
/// where no cabinet has a PC, so the pre-existing console test classes stay the reads they were. Shared for
/// <see cref="PlatformMessagingReadStubs"/>' reason — one edit for the next constructor argument, not six.
/// </summary>
internal static class PlatformRelayReadStubs
{
    public static IClinicRelayRepository NoRelays() => With(new Dictionary<Guid, ClinicRelay>());

    public static IClinicRelayRepository With(IReadOnlyDictionary<Guid, ClinicRelay> latest)
    {
        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetLatestForClinicsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);
        relays.Setup(r => r.GetLatestForClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => latest.GetValueOrDefault(id));
        return relays.Object;
    }
}
