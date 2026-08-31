using System;
using System.Collections.Generic;
using ClinicManagement.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Auth;

/// <summary>
/// The vendor console's signing key — the two refusals it was missing.
///
/// <para><c>PlatformAuthConfig</c>'s own error message has said « Elle ne doit jamais être la clé de signature des
/// cliniques » since the day it was written, and <b>nothing enforced it</b>. It also had no known-default
/// rejection, though <c>MinioCredentials</c> beside it has one and states the reason: « a credential that is only
/// decorative is treated as absent ».</para>
///
/// <para>⚠️ <b>The shared-key case is the one that matters.</b> The console and the clinics use different
/// audiences, so a shared key does not immediately let one token pass as the other — which is exactly why it
/// looks harmless and why an operator generating one secret and pasting it into both fields is the natural
/// mistake. What it actually does is collapse two trust domains into one: a single leaked value then mints both
/// a clinic session <b>and</b> a console session, and the console reads every cabinet in the portfolio.</para>
/// </summary>
public class ConsoleSigningKeyTests
{
    private const string RealClinicKey = "l0cAl-signing-key-that-is-comfortably-over-32-bytes";
    private const string RealConsoleKey = "c0ns0le-signing-key-that-is-also-over-32-bytes-long";

    [Fact]
    public void A_Console_Key_Identical_To_The_Clinic_Key_Is_Refused()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => PlatformAuthConfig.ResolveSigningKey(Config(console: RealClinicKey, clinic: RealClinicKey)));

        Assert.Contains("Auth:Local:SigningKey", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_Distinct_Keys_Are_Accepted()
    {
        var bytes = PlatformAuthConfig.ResolveSigningKey(Config(console: RealConsoleKey, clinic: RealClinicKey));

        Assert.True(bytes.Length >= 32);
    }

    /// <summary>
    /// A value straight out of this repository's own <c>.env.hosted.example</c>. It is over 32 bytes and parses
    /// fine, so every earlier check passed it — the deployment started, reported healthy, and signed the
    /// vendor's sessions with a string anyone can read on GitHub.
    /// </summary>
    [Theory]
    [InlineData("CHANGE_ME_console_signing_key_at_least_32_bytes_long")]
    [InlineData("change_me_console_signing_key_at_least_32_bytes_long")]
    [InlineData("REPLACE_ME_with_a_real_key_of_at_least_32_bytes_here")]
    public void A_Placeholder_Is_Not_A_Key(string placeholder)
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => PlatformAuthConfig.ResolveSigningKey(Config(console: placeholder, clinic: RealClinicKey)));

        Assert.Contains("exemple", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The two refusals that already existed, kept here so a rewrite cannot quietly drop them.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    public void An_Absent_Or_Short_Key_Is_Still_Refused(string? configured)
    {
        Assert.Throws<InvalidOperationException>(
            () => PlatformAuthConfig.ResolveSigningKey(Config(console: configured, clinic: RealClinicKey)));
    }

    private static IConfiguration Config(string? console, string? clinic) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PlatformAuthConfig.SigningKeyKey] = console,
                ["Auth:Local:SigningKey"] = clinic,
            })
            .Build();
}
