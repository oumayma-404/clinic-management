using ClinicManagement.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// Protection of the per-install <c>.local/db-credentials</c> file (security-hardening Part 1, audit § 2
/// finding 4 — both the <c>clinic_user</c> and the <c>postgres</c> superuser password were in cleartext under
/// Program Files).
///
/// Two behaviours carry the weight: the round trip must be exact (a mangled password makes an existing cluster
/// unreachable), and a <b>legacy plaintext</b> file written by an earlier installer must still be readable so
/// an upgrade migrates it rather than aborting (spec AC-3.3). An undecryptable file must surface as a clear
/// operator error, never as silently regenerated passwords against a live cluster (spec EC-4).
/// </summary>
public class DbCredentialProtectorTests
{
    private static DbCredentialProtector Protector() =>
        new(new EphemeralDataProtectionProvider());

    private static readonly DbCredentials Sample = new("cl1n1cUserPa55", "p0stgresSup3rPa55");

    [Fact]
    public void Round_trip_recovers_both_passwords_exactly() // [AC-3.2]
    {
        var protector = Protector();

        var fileContent = protector.ProtectFileContent(Sample);
        var read = protector.ReadFileContent(fileContent);

        Assert.Equal(Sample.ClinicUserPassword, read.Credentials.ClinicUserPassword);
        Assert.Equal(Sample.PostgresSuperPassword, read.Credentials.PostgresSuperPassword);
        Assert.False(read.WasLegacyPlaintext);
    }

    [Fact]
    public void Protected_content_does_not_contain_either_password_in_cleartext() // [AC-3.1]
    {
        var fileContent = Protector().ProtectFileContent(Sample);

        Assert.DoesNotContain(Sample.ClinicUserPassword, fileContent, StringComparison.Ordinal);
        Assert.DoesNotContain(Sample.PostgresSuperPassword, fileContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Protected_content_is_recognisable_as_protected() // how the migration path tells the forms apart
    {
        var fileContent = Protector().ProtectFileContent(Sample);

        Assert.True(DbCredentialProtector.IsProtected(fileContent));
        Assert.StartsWith(DbCredentialProtector.CipherMarker, fileContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plain-user-pw\r\nplain-super-pw\r\n")] // exactly what the previous installer wrote
    [InlineData("plain-user-pw\nplain-super-pw\n")]     // tolerate LF too
    [InlineData("  plain-user-pw  \r\n  plain-super-pw  \r\n")] // and surrounding whitespace
    public void Legacy_plaintext_file_is_read_and_flagged_for_migration(string legacyContent) // [AC-3.3]
    {
        var read = Protector().ReadFileContent(legacyContent);

        Assert.Equal("plain-user-pw", read.Credentials.ClinicUserPassword);
        Assert.Equal("plain-super-pw", read.Credentials.PostgresSuperPassword);
        Assert.True(read.WasLegacyPlaintext);
        Assert.False(DbCredentialProtector.IsProtected(legacyContent));
    }

    [Fact]
    public void Undecryptable_content_surfaces_an_operator_error_not_a_silent_regeneration() // [AC-3.4] EC-4
    {
        // Simulates the machine-rebuilt case: the file is marked protected, but this process's key ring
        // cannot open it. A fresh Ephemeral provider stands in for "a different machine's keys".
        var fileContent = Protector().ProtectFileContent(Sample);
        var otherMachine = Protector();

        var error = Assert.Throws<InvalidOperationException>(() => otherMachine.ReadFileContent(fileContent));

        // The message must point the operator at the documented recovery, not just say "failed".
        Assert.Contains("sauvegarde", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pgdata", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_content_is_rejected(string? fileContent)
    {
        Assert.Throws<InvalidOperationException>(() => Protector().ReadFileContent(fileContent));
    }

    [Fact]
    public void Marker_without_a_payload_is_rejected()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Protector().ReadFileContent(DbCredentialProtector.CipherMarker + "\r\n"));

        Assert.Contains("aucune donnée", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_single_line_payload_is_rejected() // two passwords are required, not one
    {
        var error = Assert.Throws<InvalidOperationException>(() => Protector().ReadFileContent("only-one-line\r\n"));

        Assert.Contains("incomplet", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "super")]
    [InlineData("user", "")]
    [InlineData("   ", "super")]
    public void Protecting_incomplete_credentials_is_rejected(string clinicUser, string postgres)
    {
        Assert.Throws<InvalidOperationException>(
            () => Protector().ProtectFileContent(new DbCredentials(clinicUser, postgres)));
    }

    [Fact]
    public void IsProtected_is_false_for_arbitrary_text()
    {
        Assert.False(DbCredentialProtector.IsProtected("some-password\r\nanother-password"));
        Assert.False(DbCredentialProtector.IsProtected(null));
        Assert.False(DbCredentialProtector.IsProtected(""));
    }
}
