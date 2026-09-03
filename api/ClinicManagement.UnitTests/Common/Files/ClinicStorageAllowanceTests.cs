using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Files;

/// <summary>
/// The ceiling on what one cabinet may store (<c>large-file-transfer</c> Part 4).
///
/// <para>⚠️ <b>What these can fail on that nothing else can.</b> Part 3 raised the per-file line from 25 Mo to
/// 150 Mo, and a per-file cap bounds one upload while saying nothing about ten thousand. On a hosted
/// multi-tenant box a filled disk is not a degraded cabinet — it is every cabinet stopped at once, which no
/// per-request test would ever surface.</para>
/// </summary>
public class ClinicStorageAllowanceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private const long TenGigabytes = 10L * 1024 * 1024 * 1024;

    private static (ClinicStorageAllowance allowance, Mock<IPatientFileRepository> files) Enforced(
        long usedBytes, long quotaBytes = TenGigabytes)
    {
        var files = new Mock<IPatientFileRepository>();
        files
            .Setup(r => r.GetHostedBytesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usedBytes);

        var policy = new Mock<IClinicStoragePolicy>();
        policy.SetupGet(p => p.Enforced).Returns(true);
        policy.SetupGet(p => p.QuotaBytes).Returns(quotaBytes);

        return (new ClinicStorageAllowance(files.Object, policy.Object), files);
    }

    // ── The arithmetic ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The load-bearing case: the check is on <c>used + incoming</c>, not on <c>used</c>. Comparing only what
    /// is already stored lets a cabinet one byte under its ceiling add another hundred and fifty megabytes —
    /// which, at the Part 3 line, is every single upload sailing through the last check.
    /// </summary>
    [Fact]
    public async Task A_File_That_Would_Cross_The_Ceiling_Is_Refused_Even_Though_The_Cabinet_Is_Under_It()
    {
        var (allowance, _) = Enforced(usedBytes: TenGigabytes - 1);

        var result = await allowance.EnsureRoomForAsync(ClinicId, 150L * 1024 * 1024);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicStorageRefusals.FullCode, result.Code);
    }

    /// <summary>The last file that fits is accepted — a ceiling that refuses at exactly the limit is off by one.</summary>
    [Fact]
    public async Task A_File_That_Lands_Exactly_On_The_Ceiling_Fits()
    {
        var (allowance, _) = Enforced(usedBytes: TenGigabytes - 1024);

        Assert.True((await allowance.EnsureRoomForAsync(ClinicId, 1024)).IsSuccess);
        Assert.True((await allowance.EnsureRoomForAsync(ClinicId, 1025)).IsFailure);
    }

    [Fact]
    public async Task An_Ordinary_Upload_On_An_Almost_Empty_Cabinet_Is_Accepted()
    {
        var (allowance, _) = Enforced(usedBytes: 40L * 1024 * 1024);

        Assert.True((await allowance.EnsureRoomForAsync(ClinicId, 31_252_727)).IsSuccess);
    }

    // ── Where there is nobody to protect ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ On <c>SelfHostedLan</c> the cabinet's own machine IS the object store, so a quota would be this product
    /// metering somebody's own disk back to them. Asserted on the <b>repository</b> (<c>Times.Never</c>) and not
    /// merely on the verdict: a quota that answered « fits » after reading the whole store would pass a
    /// verdict-only test while doing work no deployment there should pay for, on every single upload.
    /// </summary>
    [Fact]
    public async Task Where_The_Cabinet_Owns_The_Disk_Nothing_Is_Counted_And_Nothing_Is_Refused()
    {
        var files = new Mock<IPatientFileRepository>();
        var policy = new Mock<IClinicStoragePolicy>();
        policy.SetupGet(p => p.Enforced).Returns(false);

        var allowance = new ClinicStorageAllowance(files.Object, policy.Object);

        Assert.True((await allowance.EnsureRoomForAsync(ClinicId, long.MaxValue / 2)).IsSuccess);

        var usage = await allowance.ReadAsync(ClinicId);
        Assert.False(usage.Enforced);
        Assert.Equal(0, usage.QuotaBytes);

        files.Verify(
            r => r.GetHostedBytesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The deployment kind decides whether there is a ceiling, and configuration decides only how big it is —
    /// the same split <c>IFileResidencyPolicy</c> makes, and for the same reason: a setting able to turn the
    /// ceiling off would let one practice fill the disk under every other practice.
    /// </summary>
    [Theory]
    [InlineData("HostedMultiTenant", true)]
    [InlineData("SelfHostedLan", false)]
    public void Whether_There_Is_A_Ceiling_Is_Derived_From_The_Deployment(string kind, bool enforced)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Deployment:Profile"] = kind,
                // Even a configured size cannot conjure a ceiling where the clinic owns the disk.
                ["Deployment:StorageQuotaPerClinicBytes"] = "12345678",
            })
            .Build();

        var policy = new ClinicStoragePolicy(DeploymentProfile.Resolve(configuration), configuration);

        Assert.Equal(enforced, policy.Enforced);
        Assert.Equal(enforced ? 12345678L : 0L, policy.QuotaBytes);
    }

    /// <summary>An absent or nonsensical setting falls back rather than taking the deployment off the air.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not a number")]
    public void A_Missing_Or_Absurd_Configured_Size_Falls_Back_To_The_Default(string? configured)
    {
        var settings = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["Deployment:Profile"] = "HostedMultiTenant",
        };
        if (configured is not null)
        {
            settings["Deployment:StorageQuotaPerClinicBytes"] = configured;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var policy = new ClinicStoragePolicy(DeploymentProfile.Resolve(configuration), configuration);

        Assert.Equal(ClinicStoragePolicy.DefaultQuotaBytes, policy.QuotaBytes);
    }

    // ── The sentence ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ Read chairside, so it says what still works before what does not — <c>FileResidencyRefusals</c>' rule,
    /// which this sits beside. It also names both figures: « plein » with no numbers gives a dentist nothing to
    /// decide with, and the two ways out (delete, or ask) are the whole point of telling them at all.
    /// </summary>
    [Fact]
    public void The_Refusal_Names_Both_Figures_And_What_Still_Works()
    {
        var message = ClinicStorageRefusals.Full(
            usedBytes: (long)(9.8 * 1024 * 1024 * 1024),
            quotaBytes: TenGigabytes);

        Assert.Contains("9,8 Go", message, StringComparison.Ordinal);
        Assert.Contains("10,0 Go", message, StringComparison.Ordinal);
        Assert.Contains("restent accessibles", message, StringComparison.Ordinal);
        // French all through: a decimal point here reads as a machine error in the middle of a French sentence.
        Assert.DoesNotContain("9.8", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠️ Below a gigabyte the sentence must switch units, and this is a **real defect caught by reading the
    /// served output** rather than by reasoning: a cabinet on 7,4 Mo of a 60 Mo ceiling was told « 0,0 Go sur
    /// 0,1 Go utilisés », which states nothing at all and reads as a broken figure. Every assertion above was
    /// green throughout, because they all use gigabyte-scale fixtures.
    /// </summary>
    [Fact]
    public void Under_A_Gigabyte_The_Sentence_Speaks_In_Megabytes()
    {
        var message = ClinicStorageRefusals.Full(
            usedBytes: (long)(7.4 * 1024 * 1024),
            quotaBytes: 60L * 1024 * 1024);

        Assert.Contains("7,4 Mo", message, StringComparison.Ordinal);
        Assert.Contains("60,0 Mo", message, StringComparison.Ordinal);
        Assert.DoesNotContain("0,0 Go", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remaining_Is_Never_Negative_When_A_Cabinet_Is_Already_Over()
    {
        // A ceiling lowered under a cabinet's feet, or rows written before the quota existed: « il reste
        // -2,3 Go » is not a sentence, and a meter fed a negative renders as full rather than as nonsense.
        var (allowance, _) = Enforced(usedBytes: TenGigabytes * 2);

        var usage = await allowance.ReadAsync(ClinicId);

        Assert.Equal(0, usage.RemainingBytes);
        Assert.True((await allowance.EnsureRoomForAsync(ClinicId, 1)).IsFailure);
    }
}
