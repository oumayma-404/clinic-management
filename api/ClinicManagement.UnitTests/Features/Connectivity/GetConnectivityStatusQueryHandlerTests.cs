using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Connectivity.Queries;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Connectivity;

/// <summary>
/// Connectivity query handler (US-1). Orchestrates <see cref="IInternetProbe"/> and never surfaces an
/// error to the polling frontend — a probe failure maps to "internet unreachable".
/// </summary>
public class GetConnectivityStatusQueryHandlerTests
{
    private readonly Mock<IInternetProbe> _probe = new();

    private GetConnectivityStatusQueryHandler Handler() => new(_probe.Object);

    [Fact]
    public async Task Handle_Should_Report_Reachable_When_Probe_Succeeds()
    {
        _probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(new GetConnectivityStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.InternetReachable);
    }

    [Fact]
    public async Task Handle_Should_Report_Not_Reachable_When_Probe_Reports_Down()
    {
        _probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler().Handle(new GetConnectivityStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.InternetReachable);
    }

    [Fact]
    public async Task Handle_Should_Report_Not_Reachable_When_Probe_Throws()
    {
        _probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await Handler().Handle(new GetConnectivityStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.InternetReachable);
    }
}
