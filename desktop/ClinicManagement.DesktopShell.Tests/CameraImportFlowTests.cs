using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ClinicManagement.DesktopShell;
using ClinicManagement.DesktopShell.Camera;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The camera import end to end on a card made of folders (<c>features/camera-photo-import</c>, phase 2): read, copy
/// to this PC, hand over, keep the original, copy it to the archive disk — and the add-only copy the coffre now shares.
/// A real camera on a cable is the pilot's test; everything a card does is here.
/// </summary>
public class CameraImportFlowTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 13, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayLocal = NowUtc.ToLocalTime().Date;

    private static string WritePhoto(string card, string relative, DateTime takenLocal, string content)
    {
        var path = Path.Combine(card, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, takenLocal);
        return path;
    }

    private static string Sha256Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static CameraImportService Service(TempFolder temp) =>
        new(
            new CameraJournal(temp.Sub("journal.json")),
            new PhotoStaging(temp.Sub("staging")),
            new OriginalsStore(temp.Sub("photos")),
            () => NowUtc);

    private static CardCameraSource Card(TempFolder temp) => new(temp.Sub("card"), "card:0badcafe", "Carte (T:)");

    [Fact]
    public async Task A_Card_Offers_Its_Recent_Photos_Copied_Under_Their_Own_Names()
    {
        using var temp = new TempFolder();
        var card = temp.Sub("card");
        WritePhoto(card, @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "photo one");
        WritePhoto(card, @"DCIM\100CANON\IMG_0002.JPG", TodayLocal.AddHours(10).AddMinutes(5), "photo two");
        WritePhoto(card, @"DCIM\100CANON\IMG_0003.CR3", TodayLocal.AddHours(10), "raw");
        WritePhoto(card, @"DCIM\100CANON\IMG_0000.JPG", TodayLocal.AddDays(-30), "an old one");
        WritePhoto(card, @"DCIM\100CANON\._IMG_0001.JPG", TodayLocal.AddHours(10), "appledouble");
        WritePhoto(card, @"DCIM\MISC\AUTPRINT.JPG", TodayLocal.AddHours(10), "bookkeeping");

        var batch = await Service(temp).BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);

        Assert.Equal(new[] { "IMG_0001.JPG", "IMG_0002.JPG" }, batch.Items.Select(i => i.Name));
        Assert.Equal((1, 1), (batch.Older, batch.Raw));
        foreach (var item in batch.Items)
        {
            // The camera's own name: the page builds the capture key from it.
            Assert.Equal(item.Name, Path.GetFileName(item.Path));
            Assert.StartsWith(temp.Sub("staging"), item.Path, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("photo two", File.ReadAllText(batch.Items[1].Path));
        Assert.Equal(TodayLocal.AddHours(10).AddMinutes(5), File.GetLastWriteTime(batch.Items[1].Path));
    }

    [Fact]
    public async Task A_Filed_Photo_Keeps_Its_Original_And_Is_Never_Offered_Again()
    {
        using var temp = new TempFolder();
        var card = temp.Sub("card");
        var original = WritePhoto(card, @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "the full-size photo");
        WritePhoto(card, @"DCIM\100CANON\IMG_0002.JPG", TodayLocal.AddHours(10).AddMinutes(1), "a test shot");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        var fileId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var filed = service.Acknowledge(new CameraAck(
            batch.Items[0].ItemId, CameraAck.Filed, fileId, patientId, "Ben Salah Amine", KeepOriginal: true));
        service.Acknowledge(new CameraAck(batch.Items[1].ItemId, CameraAck.Skipped, null, null, null, false));

        Assert.NotNull(filed.Kept);
        Assert.Equal((fileId, patientId, Sha256Of("the full-size photo"), 19L),
            (filed.Kept!.FileId, filed.Kept.PatientId, filed.Kept.Sha256, filed.Kept.SizeBytes));
        var store = new OriginalsStore(temp.Sub("photos"));
        Assert.True(store.Holds(fileId));
        Assert.Equal("the full-size photo", File.ReadAllText(store.PathFor(fileId)));
        Assert.True(File.Exists(original)); // Read-only on the card, always.
        Assert.False(File.Exists(batch.Items[0].Path));
        Assert.False(File.Exists(batch.Items[1].Path));

        var index = File.ReadAllLines(temp.Sub("photos", OriginalsPathPlanner.IndexFileName));
        Assert.Equal(OriginalsPathPlanner.IndexHeader, index[0].TrimStart('﻿'));
        Assert.StartsWith($"{fileId:D};Ben Salah Amine;", index[1]);

        var again = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        Assert.Empty(again.Items);
    }

    [Fact]
    public async Task A_Clinic_Serving_Itself_Keeps_No_Second_Original()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "photo");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);

        var result = service.Acknowledge(new CameraAck(
            batch.Items[0].ItemId, CameraAck.Filed, Guid.NewGuid(), Guid.NewGuid(), "X", KeepOriginal: false));

        Assert.Null(result.Kept);
        Assert.False(Directory.Exists(temp.Sub("photos")));
        Assert.False(File.Exists(batch.Items[0].Path));
    }

    [Fact]
    public async Task A_Batch_Walked_Away_From_Is_Still_Waiting_After_A_Restart_With_No_Card()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "photo");
        var first = await Service(temp).BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);

        // The tab closed before « Ranger »; the app restarted; the card is back in the camera.
        var restarted = Service(temp);
        Assert.Equal(1, restarted.PendingCount());
        var waiting = await restarted.BuildBatchAsync(Array.Empty<ICameraSource>(), includeOlder: false);

        Assert.Equal("Photos en attente", waiting.Label);
        Assert.Equal(first.Items.Select(i => i.ItemId), waiting.Items.Select(i => i.ItemId));
        Assert.True(File.Exists(waiting.Items[0].Path));
    }

    [Fact]
    public async Task A_Failed_Filing_Keeps_The_Copy_To_Offer_Again_While_Already_On_The_Server_Drops_It()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "one");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0002.JPG", TodayLocal.AddHours(10), "two");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);

        service.Acknowledge(new CameraAck(batch.Items[0].ItemId, CameraAck.Failed, null, null, null, false));
        service.Acknowledge(new CameraAck(batch.Items[1].ItemId, CameraAck.Already, null, null, null, false));
        var again = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);

        Assert.Equal(new[] { batch.Items[0].ItemId }, again.Items.Select(i => i.ItemId));
        Assert.Equal(batch.Items[0].Path, again.Items[0].Path); // The same copy, not a second one.
    }

    [Fact]
    public async Task An_Original_The_Server_Has_Not_Written_Down_Is_Offered_Until_It_Has()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "photo");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        var fileId = Guid.NewGuid();
        service.Acknowledge(new CameraAck(batch.Items[0].ItemId, CameraAck.Filed, fileId, Guid.NewGuid(), "X", true));

        Assert.Equal(fileId, Assert.Single(Service(temp).UnrecordedOriginals()).FileId);

        service.MarkOriginalRecorded(fileId);
        Assert.Empty(Service(temp).UnrecordedOriginals());
    }

    [Fact]
    public async Task Housekeeping_Drops_A_Copy_Nobody_Answered_For_A_Week()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "photo");
        var batch = await Service(temp).BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(batch.Items[0].Path)!, NowUtc.AddDays(-9));

        var later = new CameraImportService(
            new CameraJournal(temp.Sub("journal.json")),
            new PhotoStaging(temp.Sub("staging")),
            new OriginalsStore(temp.Sub("photos")),
            () => NowUtc.AddDays(8));
        later.Housekeep();

        Assert.False(File.Exists(batch.Items[0].Path));
        Assert.Equal(0, later.PendingCount());
    }

    // ── Staging ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_Sweep_Removes_Only_Old_Copies_Nobody_Waits_For()
    {
        using var temp = new TempFolder();
        var staging = new PhotoStaging(temp.Sub("staging"));
        string Folder(string id, int ageDays)
        {
            var folder = temp.Sub("staging", id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "IMG.JPG"), "x");
            Directory.SetLastWriteTimeUtc(folder, NowUtc.AddDays(-ageDays));
            return folder;
        }

        var orphan = Folder("aaaaaaaaaaaaaaaa", 3);
        var live = Folder("bbbbbbbbbbbbbbbb", 30);
        var fresh = Folder("cccccccccccccccc", 0);

        var removed = staging.Sweep(new System.Collections.Generic.HashSet<string> { "bbbbbbbbbbbbbbbb" }, NowUtc);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(live));
        Assert.True(Directory.Exists(fresh));
    }

    // ── The add-only copy the coffre and the originals share ─────────────────────────────────────────────

    [Fact]
    public async Task A_Second_Copy_With_Nothing_New_Still_Reports_Everything_The_Destination_Holds()
    {
        using var temp = new TempFolder();
        var source = temp.Sub("source");
        Directory.CreateDirectory(Path.Combine(source, "a"));
        File.WriteAllText(Path.Combine(source, "a", "one.dcm"), "12345");
        File.WriteAllText(Path.Combine(source, "two.dcm"), "123");

        var first = await AddOnlyFolderCopy.CopyAsync(source, temp.Sub("copy"));
        var second = await AddOnlyFolderCopy.CopyAsync(source, temp.Sub("copy"));

        Assert.Equal(new AddOnlyFolderCopy.Result(2, 8, 2, 8), first);

        // The coffre's report used to send this run's bytes — 0 — and the server answered « incomplete ».
        Assert.Equal(new AddOnlyFolderCopy.Result(0, 0, 2, 8), second);
    }

    [Fact]
    public async Task The_Copy_Never_Deletes_And_Skips_A_Half_Written_File()
    {
        using var temp = new TempFolder();
        var source = temp.Sub("source");
        var copy = temp.Sub("copy");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(copy);
        File.WriteAllText(Path.Combine(source, "kept.dcm"), "abc");
        File.WriteAllText(Path.Combine(source, "arriving.dcm.part"), "ab");
        File.WriteAllText(Path.Combine(copy, "only-here.dcm"), "z");

        await AddOnlyFolderCopy.CopyAsync(source, copy);
        File.Delete(Path.Combine(source, "kept.dcm"));
        await AddOnlyFolderCopy.CopyAsync(source, copy);

        Assert.True(File.Exists(Path.Combine(copy, "kept.dcm")));
        Assert.True(File.Exists(Path.Combine(copy, "only-here.dcm")));
        Assert.False(File.Exists(Path.Combine(copy, "arriving.dcm.part")));
    }

    [Fact]
    public void Two_Folders_Of_One_Disk_Are_Refused_As_A_Copy()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Sub("a"));
        Directory.CreateDirectory(temp.Sub("b"));

        Assert.Contains("même disque", AddOnlyFolderCopy.SameDiskRefusal(temp.Sub("a"), temp.Sub("b")));
    }

    [Fact]
    public void A_Drive_That_Is_Not_There_Is_Not_Called_The_Same_Disk()
    {
        using var temp = new TempFolder();
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var absent = "ZYXWVUTSRQPONMLKJIHGFED".First(letter => !used.Contains(letter));

        Assert.Null(AddOnlyFolderCopy.SameDiskRefusal(temp.Path, $@"{absent}:\copies"));
    }

    // ── The originals' copy to the archive disk ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_Originals_Copy_Counts_Originals_Only_And_Carries_The_Index()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "full size");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        var fileId = Guid.NewGuid();
        service.Acknowledge(new CameraAck(batch.Items[0].ItemId, CameraAck.Filed, fileId, Guid.NewGuid(), "X", true));

        var outcome = await OriginalsCopyService.CopyNowAsync(
            service.Originals, temp.Sub("archive"), sameDiskRefusal: (_, _) => null);

        Assert.True(outcome.Complete);
        Assert.Equal((1, 9L), (outcome.Files, outcome.Bytes));
        var destination = temp.Sub("archive", OriginalsCopyService.DestinationFolderName);
        Assert.True(File.Exists(Path.Combine(destination, OriginalsPathPlanner.RelativePathFor(fileId))));
        Assert.True(File.Exists(Path.Combine(destination, OriginalsPathPlanner.IndexFileName)));
    }

    [Fact]
    public async Task An_Originals_Copy_On_The_Same_Disk_Or_Nowhere_Is_Not_Complete()
    {
        using var temp = new TempFolder();
        var store = new OriginalsStore(temp.Sub("photos"));
        Directory.CreateDirectory(store.Root);

        var sameDisk = await OriginalsCopyService.CopyNowAsync(store, temp.Sub("archive"));
        var nowhere = await OriginalsCopyService.CopyNowAsync(store, "");
        var nothingKept = await OriginalsCopyService.CopyNowAsync(new OriginalsStore(temp.Sub("none")), temp.Sub("archive"));

        Assert.False(sameDisk.Complete);
        Assert.Contains("même disque", sameDisk.Sentence);
        Assert.False(nowhere.Complete);
        Assert.True(nothingKept.Complete);
    }
}
