using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClinicManagement.DesktopShell.Camera;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The camera import's pure rules (<c>features/camera-photo-import</c>, phase 2): which photos a look at a card
/// offers, what the journal remembers across a restart, where a kept original lives, and which page messages are
/// believed.
/// </summary>
public class CameraPlanningTests
{
    private static readonly DateTime Today = new(2026, 11, 13);

    private static CameraItem Jpeg(string name, DateTime? taken, long size = 1000) =>
        new($@"DCIM\100CANON\{name}", name, size, taken, CameraFileKinds.Of(name));

    // ── Which photos one look offers ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Card_Never_Seen_Offers_Its_Last_Seven_Days_And_Counts_The_Rest()
    {
        var items = new[]
        {
            Jpeg("IMG_0001.JPG", Today.AddHours(10)),
            Jpeg("IMG_0002.JPG", Today.AddDays(-7).AddHours(9)),
            Jpeg("IMG_0003.JPG", Today.AddDays(-8).AddHours(23)),
        };

        var plan = CameraBatchPlanner.Plan(items, _ => null, newestDoneLocal: null, Today, includeOlder: false);

        Assert.Equal(new[] { "IMG_0002.JPG", "IMG_0001.JPG" }, plan.Deliver.Select(i => i.Name));
        Assert.Equal(1, plan.Older);
    }

    [Fact]
    public void A_Known_Card_Offers_From_The_Day_Before_Its_Newest_Filed_Photo()
    {
        var items = new[]
        {
            Jpeg("IMG_0010.JPG", Today.AddDays(-3).AddHours(8)),
            Jpeg("IMG_0011.JPG", Today.AddDays(-2).AddHours(23)),
            Jpeg("IMG_0012.JPG", Today.AddHours(9)),
        };

        // The newest filed photo is from yesterday 11:00, so the window opens the day before yesterday at 00:00.
        var plan = CameraBatchPlanner.Plan(
            items, _ => null, newestDoneLocal: Today.AddDays(-1).AddHours(11), Today, includeOlder: false);

        Assert.Equal(new[] { "IMG_0011.JPG", "IMG_0012.JPG" }, plan.Deliver.Select(i => i.Name));
        Assert.Equal(1, plan.Older);
    }

    [Fact]
    public void A_Photo_Dealt_With_Is_Never_Offered_Again_While_One_Left_Unanswered_Is()
    {
        var filed = Jpeg("IMG_0001.JPG", Today.AddHours(9));
        var already = Jpeg("IMG_0002.JPG", Today.AddHours(9));
        var skipped = Jpeg("IMG_0003.JPG", Today.AddHours(9));
        var delivered = Jpeg("IMG_0004.JPG", Today.AddHours(9));
        var failed = Jpeg("IMG_0005.JPG", Today.AddHours(9));
        var states = new Dictionary<CameraItem, CameraItemState>
        {
            [filed] = CameraItemState.Filed,
            [already] = CameraItemState.AlreadyImported,
            [skipped] = CameraItemState.Skipped,
            [delivered] = CameraItemState.Delivered,
            [failed] = CameraItemState.Failed,
        };

        var plan = CameraBatchPlanner.Plan(
            states.Keys, i => states[i], newestDoneLocal: null, Today, includeOlder: false);

        Assert.Equal(new[] { "IMG_0004.JPG", "IMG_0005.JPG" }, plan.Deliver.Select(i => i.Name));
        Assert.Equal(3, plan.Done);
    }

    [Fact]
    public void Raw_Heic_And_Video_Are_Counted_Inside_The_Window_Only()
    {
        var items = new[]
        {
            new CameraItem("a", "IMG_0001.CR3", 1, Today.AddHours(9), CameraFileKind.Raw),
            new CameraItem("b", "IMG_0002.HEIC", 1, Today.AddHours(9), CameraFileKind.Heic),
            new CameraItem("c", "MVI_0003.MP4", 1, Today.AddHours(9), CameraFileKind.Video),
            new CameraItem("d", "IMG_0004.THM", 1, Today.AddHours(9), CameraFileKind.Other),
            new CameraItem("e", "IMG_0005.CR3", 1, Today.AddDays(-60), CameraFileKind.Raw),
        };

        var plan = CameraBatchPlanner.Plan(items, _ => null, null, Today, includeOlder: false);

        Assert.Empty(plan.Deliver);
        Assert.Equal((1, 1, 1, 1), (plan.Raw, plan.Heic, plan.Video, plan.Other));
        Assert.Equal(0, plan.Older); // « plus anciennes » counts photos the page could file, not RAW files.
    }

    [Fact]
    public void A_Raw_Beside_Its_Jpeg_Is_Not_Counted_But_A_Raw_Alone_Is()
    {
        var items = new[]
        {
            Jpeg("IMG_0412.JPG", Today.AddHours(9)),
            new CameraItem(@"DCIM\100CANON\IMG_0412.CR3", "IMG_0412.CR3", 1, Today.AddHours(9), CameraFileKind.Raw),
            new CameraItem(@"DCIM\100CANON\IMG_0413.CR3", "IMG_0413.CR3", 1, Today.AddHours(9), CameraFileKind.Raw),
            // The same name in another folder is another shot.
            new CameraItem(@"DCIM\101CANON\IMG_0412.CR3", "IMG_0412.CR3", 1, Today.AddHours(9), CameraFileKind.Raw),
        };

        var plan = CameraBatchPlanner.Plan(items, _ => null, null, Today, includeOlder: false);

        Assert.Single(plan.Deliver);
        Assert.Equal(2, plan.Raw);
    }

    [Fact]
    public void Plus_Anciennes_Offers_Everything_Not_Yet_Dealt_With()
    {
        var items = new[] { Jpeg("IMG_0001.JPG", Today.AddDays(-400)), Jpeg("IMG_0002.JPG", Today.AddHours(1)) };

        var plan = CameraBatchPlanner.Plan(items, _ => null, Today.AddHours(-1), Today, includeOlder: true);

        Assert.Equal(2, plan.Deliver.Count);
        Assert.Equal(0, plan.Older);
    }

    [Fact]
    public void A_Photo_With_No_Date_Is_Offered_Rather_Than_Hidden()
    {
        var plan = CameraBatchPlanner.Plan(new[] { Jpeg("IMG_0001.JPG", null) }, _ => null, null, Today, false);

        Assert.Single(plan.Deliver);
    }

    [Fact]
    public void A_Batch_Over_The_Cap_Keeps_The_Newest_Photos()
    {
        var items = Enumerable.Range(0, CameraBatchPlanner.MaxDelivered + 5)
            .Select(i => Jpeg($"IMG_{i:D4}.JPG", Today.AddDays(-6).AddMinutes(i)))
            .ToList();

        var plan = CameraBatchPlanner.Plan(items, _ => null, null, Today, includeOlder: false);

        Assert.Equal(CameraBatchPlanner.MaxDelivered, plan.Deliver.Count);
        Assert.Equal("IMG_0005.JPG", plan.Deliver[0].Name);
        Assert.Equal(5, plan.Older);
    }

    // ── File kinds ───────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("IMG_0001.JPG", CameraFileKind.Jpeg)]
    [InlineData("_DSC0001.JPG", CameraFileKind.Jpeg)]
    [InlineData("photo.jpeg", CameraFileKind.Jpeg)]
    [InlineData("DSC_0001.NEF", CameraFileKind.Raw)]
    [InlineData("IMG_0001.CR3", CameraFileKind.Raw)]
    [InlineData("IMG_0001.HEIC", CameraFileKind.Heic)]
    [InlineData("MVI_0001.MOV", CameraFileKind.Video)]
    [InlineData("IMG_0001.THM", CameraFileKind.Other)]
    [InlineData("README", CameraFileKind.Other)]
    public void A_File_Is_Sorted_By_Its_Extension(string name, CameraFileKind kind) =>
        Assert.Equal(kind, CameraFileKinds.Of(name));

    [Fact]
    public void A_Nikon_Underscore_Name_Is_A_Photo_But_A_Card_Bookkeeping_File_Is_Not()
    {
        Assert.False(CameraFileKinds.IsIgnoredName("_DSC0001.JPG"));
        Assert.True(CameraFileKinds.IsIgnoredName("._IMG_0001.JPG"));
        Assert.True(CameraFileKinds.IsIgnoredName("Thumbs.db"));
        Assert.True(CameraFileKinds.IsIgnoredFolder("MISC"));
        Assert.True(CameraFileKinds.IsIgnoredFolder("System Volume Information"));
        Assert.False(CameraFileKinds.IsIgnoredFolder("100CANON"));

        // What a real Canon, Nikon or Sony card carries beside its photos — never « 1 autre ignoré ».
        Assert.True(CameraFileKinds.IsIgnoredName("M0100.CTG"));
        Assert.True(CameraFileKinds.IsIgnoredName("NIKON001.DSC"));
        Assert.True(CameraFileKinds.IsIgnoredName("MVI_0001.THM"));
        Assert.True(CameraFileKinds.IsIgnoredFolder("CANONMSC"));
        Assert.True(CameraFileKinds.IsIgnoredFolder("PRIVATE"));
        Assert.False(CameraFileKinds.IsIgnoredName("IMG_0001.CR3"));
    }

    // ── The journal ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_Journal_Remembers_Across_A_Restart()
    {
        using var temp = new TempFolder();
        var path = temp.Sub("journal.json");
        var item = Jpeg("IMG_0001.JPG", Today.AddHours(9));
        var now = new DateTime(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc);

        var journal = new CameraJournal(path);
        var entry = journal.Record("card:0badcafe", item, CameraItemState.Delivered, @"C:\staged\IMG_0001.JPG", "ab", now);
        journal.Update(entry.ItemId, e => e.State = CameraItemState.Filed, now);
        journal.Save();

        var reopened = new CameraJournal(path);
        Assert.Equal(CameraItemState.Filed, reopened.StateOf("card:0badcafe", item));
        Assert.Equal(Today.AddHours(9), reopened.NewestDoneLocal("card:0badcafe"));
        Assert.Null(reopened.StateOf("card:other", item));
    }

    [Fact]
    public void An_Item_Id_Is_Sixteen_Lowercase_Hex_And_Stable()
    {
        var key = CameraJournal.KeyOf("card:0badcafe", Jpeg("IMG_0001.JPG", Today));

        var id = CameraJournal.ItemIdOf(key);

        Assert.True(CameraJournal.IsItemId(id));
        Assert.Equal(id, CameraJournal.ItemIdOf(key));
        Assert.NotEqual(id, CameraJournal.ItemIdOf(CameraJournal.KeyOf("card:0badcafe", Jpeg("IMG_0001.JPG", Today, 1001))));
    }

    [Fact]
    public void A_Corrupt_Journal_Reads_As_Empty_And_Can_Be_Written_Again()
    {
        using var temp = new TempFolder();
        var path = temp.Sub("journal.json");
        File.WriteAllText(path, "{ not json");

        var journal = new CameraJournal(path);
        Assert.Equal(0, journal.Count);

        journal.Record("card:0badcafe", Jpeg("IMG_0001.JPG", Today), CameraItemState.Delivered, null, null, DateTime.UtcNow);
        journal.Save();
        Assert.Equal(1, new CameraJournal(path).Count);
    }

    [Fact]
    public void Pruning_Forgets_Old_Photos_But_Never_An_Original_The_Server_Has_Not_Written_Down()
    {
        using var temp = new TempFolder();
        var now = new DateTime(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc);
        var journal = new CameraJournal(temp.Sub("journal.json"));

        var oldDone = journal.Record("card:a1", Jpeg("A.JPG", Today), CameraItemState.Skipped, null, null, now.AddDays(-181));
        var oldUnrecorded = journal.Record("card:a1", Jpeg("B.JPG", Today), CameraItemState.Filed, null, null, now.AddDays(-181));
        journal.Update(
            oldUnrecorded.ItemId,
            e =>
            {
                e.OriginalKept = true;
                e.FileId = Guid.NewGuid();
                e.PatientId = Guid.NewGuid();
            },
            now.AddDays(-181));
        var oldPending = journal.Record(
            "card:a1", Jpeg("C.JPG", Today), CameraItemState.Delivered, @"C:\staged\C.JPG", null, now.AddDays(-8));
        var freshPending = journal.Record(
            "card:a1", Jpeg("D.JPG", Today), CameraItemState.Delivered, @"C:\staged\D.JPG", null, now.AddDays(-1));

        var removed = journal.Prune(now).Select(e => e.ItemId).ToList();

        Assert.Contains(oldDone.ItemId, removed);
        Assert.Contains(oldPending.ItemId, removed);
        Assert.DoesNotContain(oldUnrecorded.ItemId, removed);
        Assert.DoesNotContain(freshPending.ItemId, removed);
        Assert.Equal(2, journal.Count);
    }

    // ── Where an original lives ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_Original_Lives_Under_Its_File_Id_In_A_Two_Character_Folder()
    {
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

        Assert.Equal(@"3f\3fa85f64-5717-4562-b3fc-2c963f66afa6.jpg", OriginalsPathPlanner.RelativePathFor(id));
        Assert.True(OriginalsPathPlanner.IsOriginal(OriginalsPathPlanner.RelativePathFor(id)));
    }

    [Theory]
    [InlineData("index.csv")]
    [InlineData(@"3f\notes.jpg")]
    [InlineData(@"aa\3fa85f64-5717-4562-b3fc-2c963f66afa6.jpg")]
    [InlineData(@"3f\3fa85f64-5717-4562-b3fc-2c963f66afa6.jpg.part")]
    [InlineData(@"x\3f\3fa85f64-5717-4562-b3fc-2c963f66afa6.jpg")]
    public void Anything_Else_In_The_Store_Is_Not_Counted_As_An_Original(string relative) =>
        Assert.False(OriginalsPathPlanner.IsOriginal(relative));

    [Fact]
    public void The_Index_Line_Quotes_What_Needs_It_And_Neutralises_A_Formula()
    {
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

        var plain = OriginalsPathPlanner.IndexLine(id, "Ben Salah Amine", new DateTime(2026, 11, 13, 10, 5, 0), "IMG_0001.JPG", 8_000_000, "ab12");
        var tricky = OriginalsPathPlanner.IndexLine(id, "=HYPERLINK(\"x\";\"y\")", null, "a;b.JPG", 1, "cd");

        Assert.Equal(
            "3fa85f64-5717-4562-b3fc-2c963f66afa6;Ben Salah Amine;2026-11-13 10:05:00;IMG_0001.JPG;8000000;ab12", plain);
        Assert.Equal(
            "3fa85f64-5717-4562-b3fc-2c963f66afa6;\"'=HYPERLINK(\"\"x\"\";\"\"y\"\")\";;\"a;b.JPG\";1;cd", tricky);
    }

    // ── What the page may say ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Filed_Answer_Is_Read_With_Where_The_Photo_Went()
    {
        var fileId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var ack = CameraAck.TryParse(
            $$"""{"itemId":"0123456789abcdef","outcome":"filed","fileId":"{{fileId}}","patientId":"{{patientId}}","patientName":"Ben Salah Amine","keepOriginal":true}""");

        Assert.NotNull(ack);
        Assert.Equal((fileId, patientId, "Ben Salah Amine", true), (ack!.FileId!.Value, ack.PatientId!.Value, ack.PatientName, ack.KeepOriginal));
    }

    [Theory]
    [InlineData("""{"itemId":"0123456789abcdef","outcome":"filed"}""")]
    [InlineData("""{"itemId":"0123456789ABCDEF","outcome":"skipped"}""")]
    [InlineData("""{"itemId":"..\\..\\x","outcome":"skipped"}""")]
    [InlineData("""{"itemId":"0123456789abcdef","outcome":"deleted"}""")]
    [InlineData("""{"outcome":"skipped"}""")]
    [InlineData("[]")]
    [InlineData("nope")]
    public void A_Malformed_Answer_Is_Ignored(string json) => Assert.Null(CameraAck.TryParse(json));

    [Fact]
    public void A_Patient_Name_Carrying_A_Control_Character_Never_Reaches_The_Index()
    {
        var ack = CameraAck.TryParse(
            """{"itemId":"0123456789abcdef","outcome":"skipped","patientName":"Ben\nSalah","keepOriginal":"true"}""");

        Assert.NotNull(ack);
        Assert.Null(ack!.PatientName);
        Assert.False(ack.KeepOriginal); // Only a literal true.
    }

    [Theory]
    [InlineData("card:0badcafe", true)]
    [InlineData("wpd:0123456789abcdef", true)]
    [InlineData("card:", false)]
    [InlineData("CARD:0badcafe", false)]
    [InlineData("card:../etc", false)]
    [InlineData("usb:0badcafe", false)]
    public void Only_A_Source_Key_The_Shell_Could_Have_Minted_Is_Accepted(string key, bool accepted) =>
        Assert.Equal(accepted, CameraMessages.IsSourceKey(key));

    [Fact]
    public void A_Scan_Request_Keeps_Only_A_Well_Formed_Source_And_A_Literal_True()
    {
        Assert.Equal(new CameraScanRequest("card:0badcafe", true),
            CameraScanRequest.Parse("""{"sourceKey":"card:0badcafe","includeOlder":true}"""));
        Assert.Equal(new CameraScanRequest(null, false),
            CameraScanRequest.Parse("""{"sourceKey":"C:\\Windows","includeOlder":1}"""));
        Assert.Equal(new CameraScanRequest(null, false), CameraScanRequest.Parse(null));
    }

    [Fact]
    public void A_Request_Carries_A_Relay_Shaped_Id()
    {
        Assert.True(CameraMessages.TrySplitRequest("r12:{\"on\":true}", out var id, out var json));
        Assert.Equal(("r12", "{\"on\":true}"), (id, json));
        Assert.True(CameraMessages.TrySplitRequest("r3", out _, out var none));
        Assert.Null(none);
        Assert.False(CameraMessages.TrySplitRequest("x3:{}", out _, out _));
        Assert.False(CameraMessages.TrySplitRequest("r1');alert(1);//", out _, out _));
    }

    [Fact]
    public void A_File_Id_List_Is_Bounded_Deduplicated_And_Only_Guids()
    {
        var one = Guid.NewGuid();
        var many = string.Join(",", Enumerable.Range(0, 600).Select(_ => $"\"{Guid.NewGuid()}\""));

        Assert.Equal(new[] { one }, CameraMessages.FileIdsOf($$"""{"fileIds":["{{one}}","{{one}}","x",3,"{{Guid.Empty}}"]}"""));
        Assert.Equal(CameraMessages.MaxFileIds, CameraMessages.FileIdsOf($$"""{"fileIds":[{{many}}]}""").Count);
    }
}

/// <summary>A scratch folder per test, removed afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "apexa-camera-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // A file still open in a failing assertion's output: the temp folder outlives it.
        }
    }
}
