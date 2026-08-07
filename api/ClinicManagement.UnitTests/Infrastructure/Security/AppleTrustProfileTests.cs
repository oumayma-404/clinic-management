using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using ClinicManagement.Infrastructure.Security;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The iOS <c>.mobileconfig</c> the trust page serves (P8, AC-44).
///
/// Worth testing because nothing downstream can catch a malformed profile: iOS reports a bad plist as a flat
/// « Le profil n'a pas pu être installé », with no indication of which key is wrong, on a device that by
/// definition cannot reach a debugger. The load-bearing case is
/// <see cref="Two_builds_of_the_same_ca_are_byte_identical"/> — the UUIDs are derived rather than random
/// precisely so a second download re-installs in place instead of stacking a duplicate root the operator then
/// cannot tell apart.
/// </summary>
public sealed class AppleTrustProfileTests
{
    /// <summary>A throwaway CA, so the test exercises real DER rather than arbitrary bytes.</summary>
    private static byte[] SampleCaDer()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return cert.Export(X509ContentType.Cert);
    }

    private static XElement RootDict(byte[] profile)
    {
        // Parse with the DTD left alone — we care that it is well-formed XML, not that Apple's DTD resolves.
        var text = Encoding.UTF8.GetString(profile);
        var document = XDocument.Parse(text);
        return document.Root!.Element("dict")!;
    }

    private static string? ValueFor(XElement dict, string key)
    {
        var nodes = dict.Elements().ToList();
        for (var i = 0; i < nodes.Count - 1; i++)
        {
            if (nodes[i].Name == "key" && nodes[i].Value == key)
            {
                return nodes[i + 1].Value;
            }
        }

        return null;
    }

    [Fact] // [AC-44] the profile is well-formed and declares itself a configuration profile
    public void Builds_a_well_formed_configuration_profile()
    {
        var dict = RootDict(AppleTrustProfile.Build(SampleCaDer(), "Cabinet Test"));

        Assert.Equal("Configuration", ValueFor(dict, "PayloadType"));
        Assert.Equal("1", ValueFor(dict, "PayloadVersion"));
        Assert.Equal("Cabinet Test", ValueFor(dict, "PayloadOrganization"));
    }

    [Fact] // [AC-44] the payload installs a ROOT certificate, and carries the CA's own bytes unaltered
    public void Carries_the_ca_as_a_root_certificate_payload()
    {
        var caDer = SampleCaDer();
        var dict = RootDict(AppleTrustProfile.Build(caDer, "Cabinet Test"));

        var payload = dict.Element("array")!.Element("dict")!;
        Assert.Equal("com.apple.security.root", ValueFor(payload, "PayloadType"));

        // The bytes iOS will install must be the bytes we were given — a re-encode here would install a root
        // that signs nothing, and the device would fail with a generic certificate error.
        var embedded = Convert.FromBase64String(ValueFor(payload, "PayloadContent")!);
        Assert.Equal(caDer, embedded);
    }

    [Fact] // the half-install trap is the most common failure, so the profile itself has to name it
    public void Tells_the_user_to_enable_full_trust_afterwards()
    {
        var text = Encoding.UTF8.GetString(AppleTrustProfile.Build(SampleCaDer()));

        Assert.Contains("Certificats de confiance", text);
    }

    [Fact] // [AC-44] a second download must REPLACE, not stack a second indistinguishable root
    public void Two_builds_of_the_same_ca_are_byte_identical()
    {
        var caDer = SampleCaDer();

        Assert.Equal(
            AppleTrustProfile.Build(caDer, "Cabinet Test"),
            AppleTrustProfile.Build(caDer, "Cabinet Test"));
    }

    [Fact] // a REGENERATED CA is a genuinely different profile — the stale-CA failure state depends on this
    public void A_different_ca_yields_different_payload_uuids()
    {
        var first = RootDict(AppleTrustProfile.Build(SampleCaDer()));
        var second = RootDict(AppleTrustProfile.Build(SampleCaDer()));

        Assert.NotEqual(ValueFor(first, "PayloadUUID"), ValueFor(second, "PayloadUUID"));
    }

    [Fact] // iOS parses PayloadUUID strictly; a non-UUID string fails with no useful message
    public void Payload_uuids_are_parseable_guids()
    {
        var dict = RootDict(AppleTrustProfile.Build(SampleCaDer()));
        var payload = dict.Element("array")!.Element("dict")!;

        Assert.True(Guid.TryParse(ValueFor(dict, "PayloadUUID"), out _));
        Assert.True(Guid.TryParse(ValueFor(payload, "PayloadUUID"), out _));
        Assert.NotEqual(ValueFor(dict, "PayloadUUID"), ValueFor(payload, "PayloadUUID"));
    }

    [Fact] // an empty display name would render a blank title, which reads as a broken profile
    public void A_blank_clinic_label_falls_back_rather_than_rendering_empty()
    {
        var dict = RootDict(AppleTrustProfile.Build(SampleCaDer(), "   "));

        Assert.False(string.IsNullOrWhiteSpace(ValueFor(dict, "PayloadOrganization")));
    }

    [Fact]
    public void An_empty_certificate_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AppleTrustProfile.Build(Array.Empty<byte>()));
    }
}
