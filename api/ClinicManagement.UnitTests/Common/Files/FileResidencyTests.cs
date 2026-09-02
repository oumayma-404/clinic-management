using ClinicManagement.Application.Common.Files;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Files;

/// <summary>
/// Where a file lives, and the boundary that decides it — <b>the coffre's own arithmetic, which shipped with no
/// tests at all</b> (`features/clinic-file-vault/progress.md` deferred them and the pass never ran).
///
/// <para>The stakes are asymmetric in a way worth stating: a threshold that reads too <i>low</i> sends an ordinary
/// scan to a folder only one machine can open, and a threshold that reads too <i>high</i> puts a four-hundred-
/// megabyte study on the hosted disk — one live copy, fourteen nightly tarballs and one off-site copy a night,
/// over a 9 Mbps uplink. Neither raises an error anywhere.</para>
/// </summary>
public class FileResidencyTests
{
    private static FileResidencyPolicy PolicyFor(string profile)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Profile"] = profile })
            .Build();

        return new FileResidencyPolicy(DeploymentProfile.Resolve(configuration));
    }

    // ── The boundary itself ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void At_The_Threshold_Exactly_A_Study_Is_Still_Hosted()
    {
        var rule = ResidencyRule.HostedUpTo(FileTypeCatalog.DocumentBytes);

        Assert.Equal(FileResidency.Hosted, rule.Decide(FileTypeCatalog.DocumentBytes));
    }

    [Fact]
    public void One_Byte_Over_The_Threshold_It_Goes_To_The_Coffre()
    {
        var rule = ResidencyRule.HostedUpTo(FileTypeCatalog.DocumentBytes);

        Assert.Equal(FileResidency.Vault, rule.Decide(FileTypeCatalog.DocumentBytes + 1));
    }

    [Fact]
    public void An_Always_Hosted_Format_Never_Goes_To_The_Coffre_At_Any_Size()
    {
        Assert.Equal(FileResidency.Hosted, ResidencyRule.AlwaysHosted.Decide(long.MaxValue));
    }

    [Fact]
    public void A_Threshold_Must_Be_Positive()
    {
        Assert.Throws<ArgumentException>(() => ResidencyRule.HostedUpTo(0));
        Assert.Throws<ArgumentException>(() => ResidencyRule.HostedUpTo(-1));
    }

    // ── The deployment gate ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void There_Is_No_Coffre_Where_The_Clinic_Already_Holds_Its_Own_Blobs()
    {
        var policy = PolicyFor("SelfHostedLan");

        Assert.False(policy.VaultAvailable);
    }

    /// <summary>
    /// ⚠️ On a LAN install the feature is <b>absent, not refusing</b>: the bytes are already on the practice's own
    /// disk, so a study of any size stays hosted at the door's own ceiling. A `Vault` verdict there would refuse an
    /// upload that has always worked.
    /// </summary>
    [Fact]
    public void Every_Format_Stays_Hosted_At_Any_Size_Where_There_Is_No_Coffre()
    {
        var policy = PolicyFor("SelfHostedLan");

        foreach (var entry in FileTypeCatalog.All)
        {
            Assert.Equal(FileResidency.Hosted, policy.Decide(entry, long.MaxValue));
        }
    }

    [Fact]
    public void A_Hosted_Deployment_Files_A_Large_Study_At_The_Cabinet()
    {
        var policy = PolicyFor("HostedMultiTenant");
        var dicom = FileTypeCatalog.TryGet("dcm")!;

        Assert.True(policy.VaultAvailable);
        Assert.Equal(FileResidency.Hosted, policy.Decide(dicom, FileTypeCatalog.DocumentBytes));
        Assert.Equal(FileResidency.Vault, policy.Decide(dicom, FileTypeCatalog.DocumentBytes + 1));
    }

    /// <summary>
    /// A 40 Mo panoramique used to be refused outright: PNG was capped at the document ceiling and had no coffre
    /// route, so the very problem the coffre exists for had no answer for the format a clinic produces most.
    /// </summary>
    [Fact]
    public void A_Large_Panoramique_Is_Hosted_Rather_Than_Refused_Or_Sent_To_The_Coffre()
    {
        var policy = PolicyFor("HostedMultiTenant");
        var png = FileTypeCatalog.TryGet("png")!;
        var fortyMegabytes = 40L * 1024 * 1024;

        Assert.True(png.MaxBytes >= fortyMegabytes, "A 40 Mo PNG must be within the door's cap.");
        Assert.Equal(FileResidency.Hosted, policy.Decide(png, fortyMegabytes));
    }

    /// <summary>
    /// TIFF is the counter-case, and the pair is the rule: no browser paints a TIFF, and a stitched full-mouth
    /// series runs to hundreds of megabytes. Not previewable and genuinely large is what belongs at the cabinet.
    /// </summary>
    [Fact]
    public void A_Large_Tiff_Series_Goes_To_The_Coffre()
    {
        var policy = PolicyFor("HostedMultiTenant");
        var tiff = FileTypeCatalog.TryGet("tiff")!;

        Assert.Equal(FileResidency.Vault, policy.Decide(tiff, FileTypeCatalog.DocumentBytes + 1));
    }

    // ── The path, which both sides derive and neither stores ──────────────────────────────────────────────

    [Fact]
    public void The_Coffre_Path_Is_Composed_From_Ids_The_Row_Already_Carries()
    {
        var patientId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var fileId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var path = VaultPath.For(patientId, fileId, ".DCM");

        Assert.Equal($"coffre/{patientId}/{fileId}.dcm", path);
    }

    [Fact]
    public void The_Coffre_Path_Refuses_An_Empty_Identifier()
    {
        var id = Guid.NewGuid();

        Assert.ThrowsAny<Exception>(() => VaultPath.For(Guid.Empty, id, ".dcm"));
        Assert.ThrowsAny<Exception>(() => VaultPath.For(id, Guid.Empty, ".dcm"));
    }
}
