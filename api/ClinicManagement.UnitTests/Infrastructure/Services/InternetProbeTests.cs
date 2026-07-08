using System.Net;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The Singleton internet probe (US-1). Verifies status classification (2xx/3xx ⇒ reachable, failure
/// ⇒ not) and that results are cached/shared so a burst of polls collapses to one outbound probe (R-1).
/// </summary>
public class InternetProbeTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Connectivity:ProbeUrl"] = "https://probe.test/generate_204",
                ["Connectivity:ProbeTimeoutSeconds"] = "3",
                ["Connectivity:ProbeCacheSeconds"] = "30",
            })
            .Build();

    private static InternetProbe Probe(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        return new InternetProbe(
            factory.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Config(),
            NullLogger<InternetProbe>.Instance);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Found)] // 302 — still counts as reachable (2xx/3xx)
    public async Task Should_Report_Reachable_For_2xx_3xx(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        var reachable = await Probe(handler).IsInternetReachableAsync();

        Assert.True(reachable);
    }

    [Fact]
    public async Task Should_Report_Not_Reachable_When_Request_Fails()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route to host"));

        var reachable = await Probe(handler).IsInternetReachableAsync();

        Assert.False(reachable);
    }

    [Fact]
    public async Task Should_Cache_Result_Within_Ttl_And_Probe_Once()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var probe = Probe(handler);

        var first = await probe.IsInternetReachableAsync();
        var second = await probe.IsInternetReachableAsync();
        var third = await probe.IsInternetReachableAsync();

        Assert.True(first);
        Assert.True(second);
        Assert.True(third);
        Assert.Equal(1, handler.CallCount); // R-1: one outbound probe serves all polls within the TTL
    }

    /// <summary>Intercepts every outbound request and counts calls; no real network is touched.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responder(request));
        }
    }
}
