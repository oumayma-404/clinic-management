using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ClinicManagement.Infrastructure.Security;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// The object-store hop's trust rule (hosted-security-hardening Part 2, FR-2.2).
///
/// <para><b>Why this class exists at all.</b> The wiring it guards is one lambda, and the wrong version of that
/// lambda — <c>=> true</c> — is indistinguishable from the right one in a diff, compiles, makes every test pass
/// and silently turns verified TLS back into encryption with no identity behind it. So the cases here are almost
/// entirely about what must be <b>refused</b>: a name mismatch, a missing certificate, and anything that is not
/// purely a chain problem.</para>
/// </summary>
public class InternalRootTrustTests
{
    // The ordinary path: the framework already accepted it, so nothing here may narrow that.
    [Fact]
    public void A_Certificate_With_No_Policy_Error_Is_Accepted()
    {
        using var root = Root();

        Assert.True(InternalRootTrust.IsTrusted(root, null, SslPolicyErrors.None));
    }

    // The whole point of adding an anchor: a leaf signed by the deployment's own CA is in no system trust store,
    // so it arrives as a chain error and must be accepted after rebuilding against that root.
    [Fact]
    public void A_Leaf_Signed_By_The_Internal_Root_Is_Accepted_On_A_Chain_Error()
    {
        using var root = Root();
        using var leaf = LeafSignedBy(root);

        Assert.True(InternalRootTrust.IsTrusted(root, leaf, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    // ⚠️ The case that separates "added an anchor" from "removed a check". A hostname that does not match the
    // leaf is not a trust-store problem and no root makes it acceptable — accepting it would mean any container
    // holding a certificate this CA signed could answer for the object store.
    [Fact]
    public void A_Name_Mismatch_Is_Refused_Even_For_A_Leaf_The_Internal_Root_Signed()
    {
        using var root = Root();
        using var leaf = LeafSignedBy(root);

        Assert.False(InternalRootTrust.IsTrusted(root, leaf, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(InternalRootTrust.IsTrusted(
            root,
            leaf,
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public void A_Leaf_Signed_By_Some_Other_Authority_Is_Refused()
    {
        using var root = Root();
        using var strangerRoot = Root();
        using var strangerLeaf = LeafSignedBy(strangerRoot);

        Assert.False(
            InternalRootTrust.IsTrusted(root, strangerLeaf, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void No_Certificate_At_All_Is_Refused()
    {
        using var root = Root();

        Assert.False(InternalRootTrust.IsTrusted(root, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        Assert.False(InternalRootTrust.IsTrusted(root, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    private static X509Certificate2 Root()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=internal test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        return request.CreateSelfSigned(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(10));
    }

    private static X509Certificate2 LeafSignedBy(X509Certificate2 issuer)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=minio", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));

        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);

        // The chain is validated, so the leaf must sit inside its issuer's window.
        return request.Create(issuer, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(5), serial);
    }
}
