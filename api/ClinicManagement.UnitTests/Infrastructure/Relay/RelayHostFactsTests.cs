using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// Which address the PC de secours reports (<c>clinic-pc-copy</c>): the one the cabinet's devices will use to find it
/// during a cut. Found on the first end-to-end run, where a Hyper-V switch address (172.23.128.1) came first.
/// </summary>
public class RelayHostFactsTests
{
    [Fact]
    public void Only_Adapters_That_Reach_A_Gateway_Are_Reported()
    {
        var addresses = RelayHostFacts.PreferNetworkAddresses(new[]
        {
            new RelayHostFacts.HostAdapter(HasGateway: false, new[] { "172.23.128.1" }),
            new RelayHostFacts.HostAdapter(HasGateway: true, new[] { "192.168.1.35" }),
        });

        Assert.Equal(new[] { "192.168.1.35" }, addresses);
    }

    // A PC with no gateway at all (an isolated cabinet switch) still reports what it has rather than nothing.
    [Fact]
    public void Without_Any_Gateway_Every_Address_Is_Kept()
    {
        var addresses = RelayHostFacts.PreferNetworkAddresses(new[]
        {
            new RelayHostFacts.HostAdapter(false, new[] { "10.0.0.5" }),
            new RelayHostFacts.HostAdapter(false, new[] { "10.0.0.6", "10.0.0.5" }),
        });

        Assert.Equal(new[] { "10.0.0.5", "10.0.0.6" }, addresses);
    }

    [Fact]
    public void At_Most_Eight_Addresses_Are_Reported()
    {
        var many = Enumerable.Range(1, 12).Select(i => $"192.168.1.{i}").ToArray();

        Assert.Equal(8, RelayHostFacts.PreferNetworkAddresses(new[] { new RelayHostFacts.HostAdapter(true, many) }).Count);
    }
}
