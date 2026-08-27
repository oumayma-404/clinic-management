using System.Net.Sockets;
using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.Connections;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The startup-failure classifier (FR-F5 / Phase 5 S2). Pure classification — verifies the two
/// operator-facing failure modes (database unreachable, port in use) are recognised, including when
/// wrapped as an inner exception, and that unrelated failures are not misclassified.
/// </summary>
public class StartupDiagnosticsTests
{
    [Fact]
    public void Socket_failure_is_a_database_connection_failure()
    {
        Assert.True(StartupDiagnostics.IsDatabaseConnectionFailure(new SocketException()));
    }

    [Fact]
    public void Wrapped_socket_failure_is_a_database_connection_failure()
    {
        var wrapped = new Exception("connect failed", new SocketException());
        Assert.True(StartupDiagnostics.IsDatabaseConnectionFailure(wrapped));
    }

    [Fact]
    public void Timeout_is_a_database_connection_failure()
    {
        Assert.True(StartupDiagnostics.IsDatabaseConnectionFailure(new TimeoutException()));
    }

    [Fact]
    public void Unrelated_exception_is_not_a_database_connection_failure()
    {
        Assert.False(StartupDiagnostics.IsDatabaseConnectionFailure(new InvalidOperationException("boom")));
    }

    [Fact]
    public void AddressInUseException_is_address_in_use()
    {
        Assert.True(StartupDiagnostics.IsAddressInUse(new AddressInUseException("in use")));
    }

    [Fact]
    public void Socket_address_already_in_use_is_address_in_use()
    {
        Assert.True(StartupDiagnostics.IsAddressInUse(new SocketException((int)SocketError.AddressAlreadyInUse)));
    }

    [Fact]
    public void Unrelated_exception_is_not_address_in_use()
    {
        Assert.False(StartupDiagnostics.IsAddressInUse(new InvalidOperationException("boom")));
    }

    [Fact]
    public void Port_in_use_message_names_the_port()
    {
        Assert.Contains("5001", StartupDiagnostics.PortInUseMessage(5001));
    }
}
