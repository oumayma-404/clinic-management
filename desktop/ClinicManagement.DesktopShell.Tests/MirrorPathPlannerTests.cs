using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The mirror's path rules (<c>patient-file-mirror</c>, AC-3 and AC-10).
///
/// <para>⚠️ <b>This is the piece the whole feature's correctness rests on.</b> The mirror keeps no index beside
/// the folder — « do I already have this file? » is answered by computing a path and looking at the disk — so a
/// planner that is not a pure function of the manifest silently produces a mirror that re-downloads everything
/// every run, or worse, writes two patients' radiographs over one another.</para>
/// </summary>
public class MirrorPathPlannerTests
{
    private static readonly Guid Amine = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mohamed = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static MirrorEntry Entry(
        Guid patientId,
        string patientName,
        string fileName,
        Guid? fileId = null,
        DateTime? uploadedAt = null) =>
        new(
            fileId ?? Guid.NewGuid(),
            patientId,
            patientName,
            fileName,
            1024,
            uploadedAt ?? new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Local));

    private static string PathOf(IReadOnlyList<MirrorPlanItem> plan, Guid fileId) =>
        plan.Single(i => i.Entry.FileId == fileId).RelativePath;

    [Fact]
    public void A_Plain_Entry_Becomes_Patient_Folder_And_Dated_File()
    {
        var id = Guid.NewGuid();
        var plan = MirrorPathPlanner.Plan(new[] { Entry(Amine, "Ben Salah Amine", "pano.jpg", id) });

        Assert.Equal(Path.Combine("Ben Salah Amine", "2026-08-28 - pano.jpg"), PathOf(plan, id));
    }

    // AC-3 — the same manifest must yield the same tree anywhere, so planning twice cannot differ.
    [Fact]
    public void Planning_Is_Deterministic()
    {
        var manifest = new[]
        {
            Entry(Amine, "Ben Salah Amine", "pano.jpg"),
            Entry(Mohamed, "Ben Ali Mohamed", "scan.pdf"),
        };

        var first = MirrorPathPlanner.Plan(manifest).Select(i => i.RelativePath);
        var second = MirrorPathPlanner.Plan(manifest).Select(i => i.RelativePath);

        Assert.Equal(first, second);
    }

    // Two different people with the same name must never share a folder — their radiographs would be
    // indistinguishable, which is the worst outcome this feature could produce.
    [Fact]
    public void Two_Patients_Sharing_A_Name_Get_Distinct_Folders()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = MirrorPathPlanner.Plan(new[]
        {
            Entry(Amine, "Ben Ali Mohamed", "pano.jpg", a),
            Entry(Mohamed, "Ben Ali Mohamed", "pano.jpg", b),
        });

        var folderA = Path.GetDirectoryName(PathOf(plan, a));
        var folderB = Path.GetDirectoryName(PathOf(plan, b));

        Assert.NotEqual(folderA, folderB);

        // AC-3: BOTH are suffixed, never just the second — otherwise the path would depend on arrival order.
        Assert.Contains("(", folderA);
        Assert.Contains("(", folderB);
    }

    // The common cabinet keeps folders a human can read.
    [Fact]
    public void A_Unique_Patient_Name_Is_Left_Alone()
    {
        var id = Guid.NewGuid();
        var plan = MirrorPathPlanner.Plan(new[]
        {
            Entry(Amine, "Ben Salah Amine", "pano.jpg", id),
            Entry(Mohamed, "Ben Ali Mohamed", "scan.pdf"),
        });

        Assert.Equal("Ben Salah Amine", Path.GetDirectoryName(PathOf(plan, id)));
    }

    // One patient, same day, same file name, two rows. Both must survive, and the extension must too.
    [Fact]
    public void Colliding_File_Names_Are_Both_Suffixed_And_Keep_Their_Extension()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = MirrorPathPlanner.Plan(new[]
        {
            Entry(Amine, "Ben Salah Amine", "pano.jpg", a),
            Entry(Amine, "Ben Salah Amine", "pano.jpg", b),
        });

        Assert.NotEqual(PathOf(plan, a), PathOf(plan, b));
        Assert.EndsWith(".jpg", PathOf(plan, a));
        Assert.EndsWith(".jpg", PathOf(plan, b));
    }

    // The same name on two different days is not a collision — the date already separates them.
    [Fact]
    public void The_Same_Name_On_Two_Days_Is_Not_A_Collision()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = MirrorPathPlanner.Plan(new[]
        {
            Entry(Amine, "Ben Salah Amine", "pano.jpg", a, new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Local)),
            Entry(Amine, "Ben Salah Amine", "pano.jpg", b, new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Local)),
        });

        Assert.EndsWith("2026-08-27 - pano.jpg", PathOf(plan, a));
        Assert.EndsWith("2026-08-28 - pano.jpg", PathOf(plan, b));
    }

    // AC-10 — a name carrying a path separator must not become a second folder level, which would put a patient's
    // file outside the folder the planner thinks it assigned.
    [Theory]
    [InlineData("Ben Salah/Amine")]
    [InlineData("Ben Salah\\Amine")]
    [InlineData("Ben:Salah*Amine?")]
    public void Path_Characters_In_A_Name_Cannot_Create_Folders(string patientName)
    {
        var id = Guid.NewGuid();
        var plan = MirrorPathPlanner.Plan(new[] { Entry(Amine, patientName, "pano.jpg", id) });

        var relative = PathOf(plan, id);
        Assert.Equal(2, relative.Split(Path.DirectorySeparatorChar).Length);
    }

    // AC-10 — a reserved device name is not a folder Windows will create.
    [Fact]
    public void A_Reserved_Device_Name_Is_Escaped()
    {
        var id = Guid.NewGuid();
        var plan = MirrorPathPlanner.Plan(new[] { Entry(Amine, "AUX", "pano.jpg", id) });

        Assert.NotEqual("AUX", Path.GetDirectoryName(PathOf(plan, id)));
    }

    // AC-10 — a patient whose name sanitises to nothing is still mirrored, under their id.
    [Fact]
    public void A_Nameless_Patient_Is_Still_Mirrored()
    {
        var id = Guid.NewGuid();
        var plan = MirrorPathPlanner.Plan(new[] { Entry(Amine, "///", "pano.jpg", id) });

        var folder = Path.GetDirectoryName(PathOf(plan, id));
        Assert.False(string.IsNullOrWhiteSpace(folder));
        Assert.Contains(Amine.ToString("N")[..8], folder);
    }

    // A trailing dot is silently stripped by Windows, so a planner that emitted one would not recognise the
    // folder it had just created and would re-download that patient's files on every run.
    [Fact]
    public void A_Trailing_Dot_Is_Removed()
    {
        var id = Guid.NewGuid();
        var plan = MirrorPathPlanner.Plan(new[] { Entry(Amine, "Dupont.", "pano.jpg", id) });

        Assert.Equal("Dupont", Path.GetDirectoryName(PathOf(plan, id)));
    }

    // AC-10 — a long name is truncated on the FILE side, never the folder, and the extension survives.
    [Fact]
    public void A_Very_Long_Name_Is_Trimmed_On_The_File_And_Keeps_Its_Extension()
    {
        var id = Guid.NewGuid();
        var plan = MirrorPathPlanner.Plan(new[]
        {
            Entry(Amine, new string('A', 300), new string('B', 300) + ".jpg", id),
        });

        var relative = PathOf(plan, id);

        Assert.True(relative.Length <= 120, $"relative path was {relative.Length} characters");
        Assert.EndsWith(".jpg", relative);
        Assert.Equal(2, relative.Split(Path.DirectorySeparatorChar).Length);
    }

    [Fact]
    public void An_Empty_Manifest_Plans_Nothing()
    {
        Assert.Empty(MirrorPathPlanner.Plan(Array.Empty<MirrorEntry>()));
    }
}
