using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Features.Meta.Queries;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Meta;

/// <summary>
/// The policy the browser is <b>told</b> rather than left to mirror — and the one read whose whole purpose is that
/// the client and the server cannot word a refusal differently.
///
/// <para>⚠️ <b>What this can fail on that nothing else can.</b> Every assertion here compares the served
/// projection with <c>FileTypeCatalog</c> itself, so a format widened on the server and not reflected in the
/// <c>accept</c> string, a cap quoted from the wrong door, or a coffre state leaking onto a deployment that has no
/// coffre are all visible here and nowhere else: the picker would simply hide files the server accepts, which is
/// exactly the silent failure the served policy replaced.</para>
/// </summary>
public class GetUploadPolicyQueryTests
{
    private static GetUploadPolicyQueryHandler Handler(string profile)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Profile"] = profile })
            .Build();

        return new GetUploadPolicyQueryHandler(new FileResidencyPolicy(DeploymentProfile.Resolve(configuration)));
    }

    private static async Task<ClinicManagement.Application.DTOs.UploadPolicyDto> Read(
        string deployment = "HostedMultiTenant", string? profile = null)
    {
        var result = await Handler(deployment).Handle(
            new GetUploadPolicyQuery { Profile = profile }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    // ── The accept string ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ <b>Extensions, never MIME types.</b> A browser derives the type from the extension through the OS
    /// registry and Windows registers none for <c>.stl</c>, <c>.dcm</c>, <c>.ply</c> or <c>.obj</c> — so a MIME
    /// accept list could not offer a single DICOM study however many types were added to it. That is the whole
    /// reason the catalogue is keyed on the extension in the first place.
    /// </summary>
    [Fact]
    public async Task The_Accept_String_Is_Extensions_And_Offers_Every_Format_The_Door_Takes()
    {
        var policy = await Read();

        var offered = policy.Accept.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().TrimStart('.'))
            .ToHashSet(StringComparer.Ordinal);

        var expected = FileUploadProfile.PatientFile.Entries
            .SelectMany(entry => entry.Extensions)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected, offered);
        Assert.DoesNotContain("/", policy.Accept, StringComparison.Ordinal);
        Assert.Contains(".dcm", policy.Accept, StringComparison.Ordinal);
        Assert.Contains(".stl", policy.Accept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Refusal_Sentences_Are_The_Servers_Own()
    {
        var policy = await Read();

        Assert.Equal(FileUploadProfile.PatientFile.UnsupportedMessage, policy.UnsupportedMessage);
        Assert.Equal(FileUploadValidator.DeniedMessage, policy.DeniedMessage);
        Assert.NotEmpty(policy.VaultUnavailableMessage);

        foreach (var format in policy.Formats)
        {
            Assert.Equal(FileUploadValidator.TooLargeMessage(format.MaxBytes), format.TooLargeMessage);
        }
    }

    [Fact]
    public async Task The_Deny_List_Is_Served_So_The_Picker_Refuses_In_The_Same_Words()
    {
        var policy = await Read();

        Assert.Equal(
            FileTypeCatalog.DeniedExtensions.OrderBy(e => e, StringComparer.Ordinal).ToList(),
            policy.DeniedExtensions);
    }

    // ── Residency, per deployment ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC-7: where the clinic already holds its own blobs the coffre is <b>absent, not refusing</b> — so a client
    /// written against one deployment kind cannot render « conservé au cabinet » on the other.
    /// </summary>
    [Fact]
    public async Task Where_There_Is_No_Coffre_Every_Format_Reads_As_Always_Hosted()
    {
        var policy = await Read(deployment: "SelfHostedLan");

        Assert.False(policy.VaultAvailable);

        foreach (var format in policy.Formats)
        {
            Assert.Equal("hosted", format.Residency);
            Assert.Equal(0, format.VaultMaxBytes);
            Assert.Equal(string.Empty, format.VaultTooLargeMessage);
            // The hosted ceiling IS the door's ceiling there — the coffre never takes over.
            Assert.Equal(format.MaxBytes, format.HostedMaxBytes);
        }
    }

    [Fact]
    public async Task A_Study_Format_Reports_Where_The_Coffre_Takes_Over_On_A_Hosted_Deployment()
    {
        var policy = await Read();
        var dicom = policy.Formats.Single(f => f.Extensions.Contains("dcm"));

        Assert.True(policy.VaultAvailable);
        Assert.Equal("hostedUpTo", dicom.Residency);
        // The residency line, named — it used to read `DocumentBytes`, which was the same value for a different
        // reason and is what let « what a document may weigh » and « what the deployment can keep » be one knob.
        Assert.Equal(FileTypeCatalog.StudyStaysAtTheCabinetAbove, dicom.HostedMaxBytes);
        Assert.Equal(FileTypeCatalog.VaultBytes, dicom.VaultMaxBytes);
        Assert.NotEmpty(dicom.VaultTooLargeMessage);
    }

    /// <summary>
    /// A study the coffre used to swallow is now hosted (`large-file-transfer` Part 3).
    ///
    /// <para>⚠️ The case is a 40 Mo panoramique <b>as a TIFF</b>, and it is worth spelling out because the PNG of
    /// the same radiograph was already hosted — <c>ImageBytes</c> put it there, on the reasoning that what a
    /// browser can paint is worth hosting. TIFF took the coffre route for being undecodable, and then the browser
    /// learned to decode it (`clinic-file-decoders`) while the residency line stayed where it was. So the one
    /// export a clinic actually produces went to a folder openable on exactly one machine.</para>
    /// </summary>
    [Fact]
    public async Task A_Forty_Megabyte_Study_Is_Hosted_Rather_Than_Kept_At_The_Cabinet()
    {
        var policy = await Read();
        var fortyMegabytes = 40L * 1024 * 1024;

        foreach (var extension in new[] { "tiff", "dcm", "stl" })
        {
            var format = policy.Formats.Single(f => f.Extensions.Contains(extension));

            Assert.True(
                fortyMegabytes <= format.HostedMaxBytes,
                $".{extension} at 40 Mo still routes to the coffre (hosted up to {format.HostedMaxBytes} bytes)");
        }
    }

    /// <summary>
    /// The panoramique case: browser-previewable formats stay hosted at their own ceiling whatever the deployment,
    /// because a coffre file opens only where its bytes are.
    /// </summary>
    [Fact]
    public async Task A_Previewable_Image_Stays_Hosted_Even_Where_A_Coffre_Exists()
    {
        var policy = await Read();

        foreach (var format in policy.Formats.Where(f => f.IsBrowserPreviewable))
        {
            Assert.Equal("hosted", format.Residency);
        }
    }

    // ── The doors, which are not one door ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The cachet and the clinic logo share PNG and JPEG with the patient's file drawer, so serving one policy
    /// for every door would quote the drawer's ceiling on a letterhead field — which is the drift this endpoint
    /// exists to remove, reintroduced one level up.
    /// </summary>
    [Fact]
    public async Task The_Profile_Image_Door_Quotes_Its_Own_Ceiling_Not_The_Patient_Drawers()
    {
        var drawer = await Read();
        var images = await Read(profile: "profile-image");

        Assert.Equal("profile-image", images.Profile);
        Assert.True(images.MaxBytes < drawer.MaxBytes);
        Assert.Equal(FileTypeCatalog.ProfileImageBytes, images.MaxBytes);

        // And it offers only what it accepts — never the drawer's DICOM and STL.
        Assert.DoesNotContain(".dcm", images.Accept, StringComparison.Ordinal);
        Assert.Equal(2, images.Formats.Count);
    }

    [Fact]
    public async Task The_Csv_Door_Offers_The_Txt_Export_The_Server_Accepts()
    {
        var policy = await Read(profile: "csv");

        Assert.Equal("csv", policy.Profile);
        Assert.Contains(".csv", policy.Accept, StringComparison.Ordinal);
        // The hand-written `accept=".csv,text/csv"` hid exactly this one.
        Assert.Contains(".txt", policy.Accept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Absent_Profile_Still_Means_The_Patient_Drawer()
    {
        var policy = await Read(profile: null);

        Assert.Equal(FileUploadProfile.PatientFile.Name, policy.Profile);
    }

    /// <summary>
    /// The chunk size is what the browser compares a file against to decide whether to open a resumable session
    /// at all, so it has to be the size the chunk endpoint will actually demand — a client that guessed low would
    /// have every part refused as « the wrong length » and every large upload fail on part one.
    /// </summary>
    [Fact]
    public async Task The_Patient_Drawer_Publishes_The_Chunk_Size_The_Endpoint_Enforces()
    {
        var policy = await Read(profile: FileUploadProfile.PatientFile.Name);

        Assert.Equal(FileTypeCatalog.UploadChunkBytes, policy.ResumableChunkBytes);
    }

    /// <summary>
    /// Zero everywhere else, and that is the whole signal: the five `…/files/uploads` endpoints hang off the
    /// patient-file controller alone, so a browser told a cachet could be chunked would open a session against a
    /// route that does not exist — and « 404 » is not a sentence anyone can act on.
    /// </summary>
    [Theory]
    [InlineData("profile-image")]
    [InlineData("medical-document-pdf")]
    [InlineData("csv")]
    public async Task A_Door_Without_Resumable_Endpoints_Publishes_No_Chunk_Size(string profile)
    {
        var policy = await Read(profile: profile);

        Assert.Equal(0L, policy.ResumableChunkBytes);
    }

    /// <summary>
    /// An unknown door is refused rather than quietly answered with the drawer's policy — which would offer DICOM
    /// as a clinic logo and quote a ceiling six times too high.
    /// </summary>
    [Fact]
    public async Task An_Unknown_Door_Is_Refused_Rather_Than_Defaulted()
    {
        var result = await Handler("HostedMultiTenant").Handle(
            new GetUploadPolicyQuery { Profile = "not-a-door" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.NotEmpty(result.Error!);
    }
}
