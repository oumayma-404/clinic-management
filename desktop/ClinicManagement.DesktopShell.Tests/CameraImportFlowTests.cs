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

    // The page's answers are saved per burst: a test that reads the disk back says when, with Flush.
    private static CameraImportService Service(TempFolder temp) =>
        new(
            new CameraJournal(temp.Sub("journal.json"), Timeout.InfiniteTimeSpan),
            new PhotoStaging(temp.Sub("staging")),
            new OriginalsStore(temp.Sub("photos")),
            () => NowUtc);

    private static CardCameraSource Card(TempFolder temp, int maxFiles = CardCameraSource.MaxFiles) =>
        new(temp.Sub("card"), "card:0badcafe", "Carte (T:)", maxFiles);

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
        service.Flush();

        Assert.Equal(fileId, Assert.Single(Service(temp).OriginalsToAnnounce()).FileId);

        service.MarkOriginalRecorded(fileId);
        service.Flush();
        Assert.Empty(Service(temp).OriginalsToAnnounce());
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
        Directory.CreateDirectory(temp.Sub("archive")); // The archive copy made it on the archive disk.

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
        Directory.CreateDirectory(temp.Sub("archive"));

        var sameDisk = await OriginalsCopyService.CopyNowAsync(store, temp.Sub("archive"));
        var nowhere = await OriginalsCopyService.CopyNowAsync(store, "");
        var nothingKept = await OriginalsCopyService.CopyNowAsync(new OriginalsStore(temp.Sub("none")), temp.Sub("archive"));

        Assert.False(sameDisk.Complete);
        Assert.Contains("même disque", sameDisk.Sentence);
        Assert.False(nowhere.Complete);
        Assert.True(nothingKept.Complete);
    }

    [Fact]
    public async Task An_Originals_Copy_Refused_By_The_Archive_Disk_Creates_Nothing_There()
    {
        using var temp = new TempFolder();
        var store = new OriginalsStore(temp.Sub("photos"));
        Directory.CreateDirectory(store.Root);
        Directory.CreateDirectory(temp.Sub("card"));

        // The archive disk is unplugged and the camera's card took its letter.
        var outcome = await OriginalsCopyService.CopyNowAsync(
            store, temp.Sub("card"), diskRefusal: _ => "carte", sameDiskRefusal: (_, _) => null);

        Assert.False(outcome.Complete);
        Assert.Equal("carte", outcome.Sentence);
        Assert.False(Directory.Exists(temp.Sub("card", OriginalsCopyService.DestinationFolderName)));
    }

    [Fact]
    public async Task A_Locked_Index_On_The_Archive_Disk_Does_Not_Fail_A_Copy_Whose_Originals_Arrived()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "full size");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        service.Acknowledge(new CameraAck(batch.Items[0].ItemId, CameraAck.Filed, Guid.NewGuid(), Guid.NewGuid(), "X", true));
        var destination = temp.Sub("archive", OriginalsCopyService.DestinationFolderName);
        Directory.CreateDirectory(destination);

        // A technician left the archive disk's index open in a spreadsheet.
        await using (new FileStream(
                         Path.Combine(destination, OriginalsPathPlanner.IndexFileName),
                         FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var outcome = await OriginalsCopyService.CopyNowAsync(
                service.Originals, temp.Sub("archive"), diskRefusal: _ => null, sameDiskRefusal: (_, _) => null);

            Assert.True(outcome.Complete);
            Assert.Equal(1, outcome.Files);
            Assert.Contains("index.csv", outcome.Sentence);
        }

        Assert.Empty(Directory.EnumerateFiles(destination, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task An_Index_Open_Elsewhere_Does_Not_Undo_A_Kept_Original_And_Gets_Its_Line_Later()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "full size");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        var fileId = Guid.NewGuid();
        service.Originals.Prepare();
        var index = temp.Sub("photos", OriginalsPathPlanner.IndexFileName);

        CameraAckResult filed;
        using (new FileStream(index, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            filed = service.Acknowledge(new CameraAck(batch.Items[0].ItemId, CameraAck.Filed, fileId, Guid.NewGuid(), "Ben Salah Amine", true));
        }

        Assert.NotNull(filed.Kept);
        Assert.Null(filed.Refusal);
        Assert.True(service.Originals.Holds(fileId));
        Assert.Empty(Directory.EnumerateFiles(temp.Sub("photos"), "*.tmp"));

        service.Originals.FlushIndex();
        Assert.StartsWith($"{fileId:D};Ben Salah Amine;", File.ReadAllLines(index)[1]);
    }

    [Fact]
    public async Task Keeping_Originals_Appends_To_The_Index_And_Never_Rewrites_It()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "one");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0002.JPG", TodayLocal.AddHours(11), "two");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        var index = temp.Sub("photos", OriginalsPathPlanner.IndexFileName);

        service.Acknowledge(new CameraAck(batch.Items[0].ItemId, CameraAck.Filed, Guid.NewGuid(), Guid.NewGuid(), "A", true));
        var first = File.ReadAllBytes(index);
        service.Acknowledge(new CameraAck(batch.Items[1].ItemId, CameraAck.Filed, Guid.NewGuid(), Guid.NewGuid(), "B", true));
        var second = File.ReadAllBytes(index);

        Assert.Equal(first, second.Take(first.Length));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, second.Take(3)); // Once, at the start: Excel reads « é ».
        Assert.Equal(3, File.ReadAllLines(index).Length);
    }

    [Fact]
    public async Task A_Burst_Of_Answers_Is_Written_Once_And_A_Flush_Puts_It_On_Disk()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "one");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0002.JPG", TodayLocal.AddHours(10), "two");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0003.JPG", TodayLocal.AddHours(10), "three");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);

        foreach (var item in batch.Items)
        {
            service.Acknowledge(new CameraAck(item.ItemId, CameraAck.Already, null, null, null, false));
        }

        // Not one write per answer: the disk still has the batch as it was handed over.
        var before = new CameraJournal(temp.Sub("journal.json"));
        Assert.All(batch.Items, i => Assert.Equal(CameraItemState.Delivered, before.ByItemId(i.ItemId)!.State));

        service.Flush();

        var after = new CameraJournal(temp.Sub("journal.json"));
        Assert.All(batch.Items, i => Assert.Equal(CameraItemState.AlreadyImported, after.ByItemId(i.ItemId)!.State));
    }

    [Fact]
    public async Task A_Full_Card_Still_Offers_Todays_Photos()
    {
        using var temp = new TempFolder();
        for (var i = 1; i <= 6; i++)
        {
            WritePhoto(temp.Sub("card"), $@"DCIM\100CANON\IMG_{i:D4}.JPG", TodayLocal.AddDays(-30).AddMinutes(i), $"old {i}");
        }

        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0007.JPG", TodayLocal.AddHours(9), "today 1");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0008.JPG", TodayLocal.AddHours(10), "today 2");

        // A cap of five on a card of eight: listed in the card's own order, today's two were never seen.
        var batch = await Service(temp).BuildBatchAsync(new[] { Card(temp, maxFiles: 5) }, includeOlder: false);

        Assert.Equal(new[] { "IMG_0007.JPG", "IMG_0008.JPG" }, batch.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task A_Card_Lists_Its_Newest_Folder_And_Files_When_It_Holds_More_Than_It_Reads()
    {
        using var temp = new TempFolder();
        for (var i = 1; i <= 6; i++)
        {
            WritePhoto(temp.Sub("card"), $@"DCIM\100CANON\IMG_{i:D4}.JPG", TodayLocal.AddDays(-20).AddMinutes(i), "old");
            WritePhoto(temp.Sub("card"), $@"DCIM\101CANON\IMG_{i:D4}.JPG", TodayLocal.AddDays(-1).AddMinutes(i), "new");
        }

        // Two kept of twelve, and only eight read: the newest folder first, its newest files kept.
        var listing = await Card(temp, maxFiles: 2).ListAsync(CancellationToken.None);

        Assert.Equal(
            new[] { @"DCIM\101CANON\IMG_0006.JPG", @"DCIM\101CANON\IMG_0005.JPG" },
            listing.Select(i => i.ObjectId));
    }

    [Fact]
    public async Task A_Source_That_Fails_Mid_Copy_Leaves_No_Part_And_Does_Not_Stop_The_Rest()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(9), "one");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0002.JPG", TodayLocal.AddHours(10), "two — the cable comes out here");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0003.JPG", TodayLocal.AddHours(11), "three");

        var batch = await Service(temp).BuildBatchAsync(
            new ICameraSource[] { new FailingSource(Card(temp), "IMG_0002.JPG") }, includeOlder: false);

        Assert.Equal(1, batch.Failed);
        Assert.Equal(new[] { "IMG_0001.JPG", "IMG_0003.JPG" }, batch.Items.Select(i => i.Name));
        Assert.All(batch.Items, i => Assert.True(File.Exists(i.Path)));
        Assert.Empty(Directory.EnumerateFiles(temp.Sub("staging"), "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Importer_Says_It_Is_Alive_Before_Each_Photo_It_Copies()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(9), "one");
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0002.JPG", TodayLocal.AddHours(10), "two");
        var reports = new List<CameraScanProgress>();

        await Service(temp).BuildBatchAsync(
            new[] { Card(temp) }, includeOlder: false, new InlineProgress<CameraScanProgress>(reports.Add));

        Assert.Equal(
            new[] { new CameraScanProgress(0, 2), new CameraScanProgress(0, 2), new CameraScanProgress(1, 2) },
            reports);
    }

    // ── Originals the server will not write down ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Kept_Original_The_Server_Never_Writes_Down_Stops_Being_Announced_But_Stays()
    {
        using var temp = new TempFolder();
        WritePhoto(temp.Sub("card"), @"DCIM\100CANON\IMG_0001.JPG", TodayLocal.AddHours(10), "photo");
        var service = Service(temp);
        var batch = await service.BuildBatchAsync(new[] { Card(temp) }, includeOlder: false);
        var fileId = Guid.NewGuid();
        service.Acknowledge(new CameraAck(batch.Items[0].ItemId, CameraAck.Filed, fileId, Guid.NewGuid(), "X", true));

        for (var load = 0; load < CameraImportService.MaxAnnouncements; load++)
        {
            Assert.Single(service.OriginalsToAnnounce());
        }

        Assert.Empty(service.OriginalsToAnnounce());
        service.Flush();
        Assert.Single(new CameraJournal(temp.Sub("journal.json")).UnrecordedOriginals());
        Assert.True(service.Originals.Holds(fileId));
    }

    [Fact]
    public void One_Page_Load_Announces_Less_Than_The_Pages_Queue_And_The_Least_Sent_First()
    {
        using var temp = new TempFolder();
        var journal = new CameraJournal(temp.Sub("journal.json"), Timeout.InfiniteTimeSpan);
        var store = new OriginalsStore(temp.Sub("photos"));
        var fresh = new HashSet<Guid>();
        for (var i = 0; i < CameraImportService.MaxAnnouncedPerLoad + 5; i++)
        {
            var item = new CameraItem($@"DCIM\100CANON\IMG_{i:D4}.JPG", $"IMG_{i:D4}.JPG", 10, TodayLocal, CameraFileKind.Jpeg);
            var entry = journal.Record("card:0badcafe", item, CameraItemState.Filed, null, "ab", NowUtc);
            var fileId = Guid.NewGuid();
            var stale = i < 5;
            journal.Update(
                entry.ItemId,
                e =>
                {
                    e.OriginalKept = true;
                    e.FileId = fileId;
                    e.PatientId = Guid.NewGuid();
                    e.Announced = stale ? 3 : 0;
                },
                NowUtc);
            Directory.CreateDirectory(Path.GetDirectoryName(store.PathFor(fileId))!);
            File.WriteAllText(store.PathFor(fileId), "original");
            if (!stale)
            {
                fresh.Add(fileId);
            }
        }

        var announced = new CameraImportService(journal, new PhotoStaging(temp.Sub("staging")), store, () => NowUtc)
            .OriginalsToAnnounce();

        Assert.True(announced.Count < 50); // VaultBridge's queue for a page that is not listening yet.
        Assert.Equal(fresh.OrderBy(g => g), announced.Select(n => n.FileId).OrderBy(g => g));
    }

    // ── One rule for « this is a camera's card » ─────────────────────────────────────────────────────────

    [Fact]
    public void A_Fixed_Disk_Holding_DCIM_Is_Not_A_Camera_Card()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Sub("DCIM", "100CANON"));

        // The watcher and « Chercher l'appareil » ask the same question: a notice for a disk the scan cannot find
        // sent the scan to every other device.
        Assert.False(CameraSources.IsCard(temp.Path));
        Assert.Null(CameraSources.CardAt(temp.Path));
    }

    /// <summary>A card whose cable comes out on one photo, after part of it was read.</summary>
    private sealed class FailingSource(ICameraSource inner, string failingName) : ICameraSource
    {
        public string SourceKey => inner.SourceKey;

        public string Label => inner.Label;

        public Task<IReadOnlyList<CameraItem>> ListAsync(CancellationToken cancellationToken) =>
            inner.ListAsync(cancellationToken);

        public async Task CopyToAsync(CameraItem item, Stream destination, CancellationToken cancellationToken)
        {
            if (item.Name != failingName)
            {
                await inner.CopyToAsync(item, destination, cancellationToken);
                return;
            }

            await destination.WriteAsync(Encoding.UTF8.GetBytes("half a"), cancellationToken);
            throw new IOException("Le périphérique a été débranché.");
        }
    }

    /// <summary>An <see cref="IProgress{T}"/> that reports where it is called — <see cref="Progress{T}"/> posts.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
