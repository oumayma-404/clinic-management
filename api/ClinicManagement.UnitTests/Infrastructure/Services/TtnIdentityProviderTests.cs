using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// [US-4] The precedence rule behind « whose certificate signs this clinic's invoices » — the one thing
/// <c>multi-tenant-cloud</c> Part D actually adds, and the reason it is a provider rather than two copies of an
/// <c>if</c> in the signer and the TTN client.
///
/// <para>The case that carries the part is <see cref="A_Hosted_Clinic_Without_Its_Own_Certificate_Is_Refused"/>:
/// the per-install certificate must <b>not</b> stand in on a multi-clinic deployment. A TEIF signature attests
/// who issued the invoice and TTN validation is irreversible, so signing with the wrong practice's qualified key
/// is not a degraded service — it is a false legal declaration that cannot be withdrawn.</para>
/// </summary>
public class TtnIdentityProviderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly byte[] ClinicCertificate = Encoding.UTF8.GetBytes("clinic-pfx-bytes");

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<ITtnSecretProtector> _protector = new();

    public TtnIdentityProviderTests()
    {
        _storage.Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(ClinicCertificate));

        // A stand-in for Data Protection: the round trip is what matters here, not the cryptography.
        _protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns((string cipher) => "plain:" + cipher);
    }

    /// <summary>
    /// ⚠️ <c>Ttn:CertPath</c> is pinned to a path that cannot exist, so the per-install branch is deterministic:
    /// left to its default it resolves <c>.local/teif-signing.pfx</c> under the test assembly's own directory,
    /// and the fall-back cases would then pass or fail depending on what happens to be on the dev machine.
    /// </summary>
    private TtnIdentityProvider Provider(DeploymentKind kind) => new(
        _clinics.Object,
        _storage.Object,
        _protector.Object,
        DeploymentProfile.For(kind),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ttn:CertPath"] = Path.Combine(Path.GetTempPath(), $"teif-absent-{Guid.NewGuid():N}.pfx")
            })
            .Build(),
        NullLogger<TtnIdentityProvider>.Instance);

    private Clinic GivenClinic(bool withOwnIdentity)
    {
        var clinic = new Clinic(ClinicId, "Cabinet Test");
        if (withOwnIdentity)
        {
            clinic.SetTtnIdentity("clinic-user", "cipher-secret", "clinics/a/teif.pfx", "cipher-password");
        }

        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        return clinic;
    }

    // [US-4] A clinic with its own identity uses it — the certificate comes from storage, the secrets decrypted.
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan)]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.CloudBrowser)]
    public async Task A_Clinic_With_Its_Own_Identity_Uses_It_In_Every_Profile(DeploymentKind kind)
    {
        GivenClinic(withOwnIdentity: true);

        var identity = await Provider(kind).ResolveAsync(ClinicId);

        Assert.Equal(TtnIdentitySource.Clinic, identity.Source);
        Assert.Equal(ClinicCertificate, identity.CertificateBytes);
        Assert.Equal("clinic-user", identity.Username);
        Assert.Equal("plain:cipher-secret", identity.ApiSecret);
        Assert.Equal("plain:cipher-password", identity.CertificatePassword);
        Assert.True(identity.HasApiCredentials);
    }

    /// <summary>
    /// [US-4] The refusal this part exists for. On a multi-clinic deployment the per-install certificate is
    /// somebody else's qualified identity, so a clinic without one is refused — loudly, with a message naming
    /// what to provide. <c>EInvoiceService</c> turns that into a queued retry rather than a wrong signature.
    /// </summary>
    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.CloudBrowser)]
    public async Task A_Hosted_Clinic_Without_Its_Own_Certificate_Is_Refused(DeploymentKind kind)
    {
        GivenClinic(withOwnIdentity: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(kind).ResolveAsync(ClinicId));

        Assert.Contains("certificat", ex.Message, StringComparison.OrdinalIgnoreCase);
        _storage.Verify(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// [US-4][R-2] The single-clinic install keeps the fall-back it has always had. It refuses here only because
    /// no PFX is on disk in a test run — the point is that it got past the topology gate and went looking, which
    /// the hosted profiles above do not.
    /// </summary>
    [Fact]
    public async Task A_Single_Clinic_Install_Falls_Back_To_The_Per_Install_Certificate()
    {
        GivenClinic(withOwnIdentity: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(DeploymentKind.SelfHostedLan).ResolveAsync(ClinicId));

        Assert.Contains(".local", ex.Message);
        Assert.DoesNotContain("multi-cabinets", ex.Message);
    }

    /// <summary>
    /// [US-4] A clinic that HAS been given a certificate never silently falls back to the install's, even where
    /// the topology would allow one — quietly substituting an identity for a clinic that was explicitly given one
    /// is how the wrong practice ends up on an irreversible declaration.
    /// </summary>
    [Fact]
    public async Task An_Unreadable_Clinic_Certificate_Refuses_Rather_Than_Falling_Back()
    {
        GivenClinic(withOwnIdentity: true);
        _storage.Setup(s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("gone"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(DeploymentKind.SelfHostedLan).ResolveAsync(ClinicId));

        Assert.Contains("stockage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // [US-4] A secret the key ring can no longer open is reported, not swallowed as « no credentials »: the
    // operator has to know which value to re-enter, and a silent null would submit as an anonymous caller.
    [Fact]
    public async Task A_Secret_That_Cannot_Be_Decrypted_Is_Reported()
    {
        GivenClinic(withOwnIdentity: true);
        _protector.Setup(p => p.Unprotect(It.IsAny<string>())).Throws(new InvalidOperationException("bad key"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(DeploymentKind.HostedMultiTenant).ResolveAsync(ClinicId));

        Assert.Contains("déchiffrer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // [US-4] An unknown clinic is refused before any storage or key-ring work.
    [Fact]
    public async Task An_Unknown_Clinic_Is_Refused()
    {
        _clinics.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(DeploymentKind.HostedMultiTenant).ResolveAsync(ClinicId));
    }
}
