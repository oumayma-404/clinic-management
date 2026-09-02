using System;
using System.Linq;
using ClinicManagement.Application.Common.Files;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Files;

/// <summary>
/// The catalogue's own internal consistency — <b>the class <c>FileTypeCatalog.cs:28</c> has named as its guard
/// since it was written, and which did not exist.</b>
///
/// <para>That is worth stating plainly: the production comment reads « a <c>const</c> because an attribute
/// argument has to be one; <c>FileTypeCatalogTests</c> pins it against the entries so it cannot fall behind a
/// widened one », and nothing pinned it. <c>MaxBytesAcrossCatalog</c> is what sizes
/// <c>[RequestSizeLimit]</c> on the patient-file door, so an entry widened past it would be refused by Kestrel
/// before model binding — a framework 413 the app never sees and cannot explain in French, which is the exact
/// failure that attribute exists to prevent.</para>
///
/// <para>Every assertion here is <b>derived from the entries</b> rather than restating today's table: a test that
/// listed the formats would have to be edited by the same person adding one, which is precisely when it stops
/// being a check.</para>
/// </summary>
public class FileTypeCatalogTests
{
    [Fact]
    public void MaxBytesAcrossCatalog_Is_The_Largest_Cap_Any_Entry_Carries()
    {
        var largest = FileTypeCatalog.All.Max(entry => entry.MaxBytes);

        Assert.Equal(largest, FileTypeCatalog.MaxBytesAcrossCatalog);
    }

    // The door's [RequestSizeLimit] is sized from the const, so an entry above it is unreachable through the
    // one endpoint that accepts every format — refused by Kestrel, in English, before any handler runs.
    [Fact]
    public void No_Entry_Is_Larger_Than_The_Ceiling_The_Upload_Action_Declares()
    {
        var oversized = FileTypeCatalog.All
            .Where(entry => entry.MaxBytes > FileTypeCatalog.MaxBytesAcrossCatalog)
            .Select(entry => entry.Label)
            .ToList();

        Assert.True(oversized.Count == 0,
            "These formats are capped above MaxBytesAcrossCatalog, so the patient-file door would 413 them "
            + $"before the handler runs: {string.Join(", ", oversized)}.");
    }

    // The deny-list is consulted BEFORE the allow-list (FileUploadValidator.ResolveEntry), so an extension in
    // both would be refused whatever the catalogue says — order-dependent behaviour nobody declared.
    [Fact]
    public void No_Accepted_Extension_Is_Also_Deny_Listed()
    {
        var accepted = FileTypeCatalog.All.SelectMany(entry => entry.Extensions).ToHashSet(StringComparer.Ordinal);
        var both = accepted.Intersect(FileTypeCatalog.DeniedExtensions, StringComparer.Ordinal).ToList();

        Assert.True(both.Count == 0,
            $"Accepted and deny-listed at once, so the deny-list silently wins: {string.Join(", ", both)}.");
    }

    // `ByExtension` is a dictionary, so a duplicate would throw at type-initialisation — which surfaces as a
    // TypeInitializationException on the first upload rather than as a failing test. Assert it directly.
    [Fact]
    public void No_Extension_Is_Claimed_By_Two_Entries()
    {
        var duplicates = FileTypeCatalog.All
            .SelectMany(entry => entry.Extensions)
            .GroupBy(extension => extension, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"Claimed by more than one entry: {string.Join(", ", duplicates)}.");
    }

    [Fact]
    public void Every_Extension_Is_Lower_Case_And_Dot_Less()
    {
        foreach (var extension in FileTypeCatalog.All.SelectMany(entry => entry.Extensions))
        {
            Assert.Equal(extension.ToLowerInvariant(), extension);
            Assert.DoesNotContain(".", extension, StringComparison.Ordinal);
        }
    }

    // AC-2.2: « A `None` with an empty reason is a build failure, not a code-review question. » The reason is
    // what the refusal message and the next reader both rely on.
    [Fact]
    public void Every_Signature_Rule_Without_A_Marker_States_Why()
    {
        foreach (var entry in FileTypeCatalog.All.Where(e => e.Signature.Kind == SignatureKind.None))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Signature.Reason),
                $"{entry.Label} declares no signature and gives no reason for it.");
        }
    }

    // A vault-eligible format must declare a coffre ceiling, or `RegisterVaultFileCommand`'s
    // `FileSize > entry.VaultMaxBytes` check refuses every file of it — a format that routes to a door nothing
    // can get through.
    [Fact]
    public void Every_Coffre_Eligible_Format_Declares_A_Coffre_Ceiling()
    {
        foreach (var entry in FileTypeCatalog.All.Where(e => e.Residency.Kind == ResidencyKind.HostedUpTo))
        {
            Assert.True(entry.VaultMaxBytes > entry.Residency.HostedMaxBytes,
                $"{entry.Label} routes to the coffre above {entry.Residency.HostedMaxBytes} bytes but its coffre "
                + $"ceiling is {entry.VaultMaxBytes}, so no size can reach it.");
        }
    }

    // The line the coffre exists for. A format a browser can paint is worth hosting whatever its size, because a
    // coffre file opens only where its bytes are; one it cannot decode is what belongs at the cabinet.
    [Fact]
    public void No_Browser_Previewable_Format_Is_Sent_To_The_Coffre()
    {
        var previewableAtTheCabinet = FileTypeCatalog.All
            .Where(entry => entry.IsBrowserPreviewable && entry.Residency.Kind == ResidencyKind.HostedUpTo)
            .Select(entry => entry.Label)
            .ToList();

        Assert.True(previewableAtTheCabinet.Count == 0,
            "A format a browser can render must stay hosted — sending it to the coffre makes it unopenable from "
            + $"a phone, from home, from the second chair: {string.Join(", ", previewableAtTheCabinet)}.");
    }

    [Fact]
    public void Every_Entry_Carries_A_French_Label_And_A_Content_Type()
    {
        foreach (var entry in FileTypeCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
            Assert.False(string.IsNullOrWhiteSpace(entry.ContentType));
            Assert.NotEmpty(entry.Extensions);
            Assert.True(entry.MaxBytes > 0);
        }
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("dcm")]
    [InlineData("tiff")]
    public void TryGet_Finds_An_Entry_By_Its_Own_Extension(string extension)
    {
        Assert.NotNull(FileTypeCatalog.TryGet(extension));
    }

    [Fact]
    public void TryGet_Answers_Null_For_An_Unknown_Extension()
    {
        Assert.Null(FileTypeCatalog.TryGet("mp4"));
        Assert.Null(FileTypeCatalog.TryGet(""));
    }
}
