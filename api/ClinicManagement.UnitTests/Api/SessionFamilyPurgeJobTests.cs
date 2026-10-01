using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The daily purge also trims spent signups and password resets — they used to be trimmed only when somebody new
/// asked, so a quiet deployment kept consumed rows past retention and `verify-schema` reported them.
/// </summary>
public class SessionFamilyPurgeJobTests
{
    private readonly Mock<ISessionFamilyRepository> _sessions = new();
    private readonly Mock<IClinicSignupRepository> _signups = new();
    private readonly Mock<IPasswordResetRequestRepository> _resets = new();

    private SessionFamilyPurgeJob Job() => new(
        _sessions.Object, _signups.Object, _resets.Object,
        new Mock<IAuditActorProvider>().Object, new Mock<ITenantScope>().Object,
        NullLogger<SessionFamilyPurgeJob>.Instance);

    [Fact]
    public async Task Purges_Spent_Signups_And_Resets_With_Their_Own_Retention()
    {
        _signups.Setup(r => r.PurgeSpentAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _resets.Setup(r => r.PurgeSpentAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await Job().PurgeExpiredSessions();

        _sessions.Verify(r => r.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _signups.Verify(r => r.PurgeSpentAsync(
            It.IsAny<DateTime>(), SignUpClinicCommandHandler.ConsumedRetention, It.IsAny<CancellationToken>()), Times.Once);
        _resets.Verify(r => r.PurgeSpentAsync(
            It.IsAny<DateTime>(), RequestPasswordResetCommandHandler.ConsumedRetention, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Keeps_Purging_Until_A_Batch_Removes_Nothing()
    {
        _signups.SetupSequence(r => r.PurgeSpentAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(200).ReturnsAsync(37).ReturnsAsync(0);
        _resets.Setup(r => r.PurgeSpentAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await Job().PurgeExpiredSessions();

        _signups.Verify(r => r.PurgeSpentAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }
}
