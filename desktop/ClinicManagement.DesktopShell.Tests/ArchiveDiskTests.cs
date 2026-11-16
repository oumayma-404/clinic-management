using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// Whether a copy lands on the archive disk and not on whatever took its letter, whether two folders share a physical
/// disk, and what the coffre's copy does when they do (<c>features/camera-photo-import</c>).
/// </summary>
public class ArchiveDiskTests
{
    private static readonly uint[] NoDisk = Array.Empty<uint>();

    // ── The archive disk, not whatever holds its letter ──────────────────────────────────────────────────

    [Fact]
    public void A_Camera_Card_Holding_The_Archive_Letter_Is_Refused_Whatever_Was_Recorded()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Sub("archive"));

        var refusal = ArchiveDisk.Refusal(temp.Sub("archive"), new uint[] { 7 }, _ => 7, isCameraCard: _ => true);

        Assert.Contains("carte d'un appareil photo", refusal);
        Assert.Contains("carte d'un appareil photo", ArchiveDisk.Unplugged(temp.Sub("archive"), isCameraCard: _ => true));
    }

    [Fact]
    public void Another_Disk_Under_The_Archive_Letter_Is_Refused_And_Either_Of_Two_Disks_Used_In_Turn_Is_Not()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Sub("archive"));
        var folder = temp.Sub("archive");

        Assert.Contains("autre disque", ArchiveDisk.Refusal(folder, new uint[] { 1 }, _ => 2, _ => false));
        Assert.Null(ArchiveDisk.Refusal(folder, new uint[] { 1, 2 }, _ => 2, _ => false));

        // A volume Windows will not describe (a share) is not called a stranger.
        Assert.Null(ArchiveDisk.Refusal(folder, new uint[] { 1 }, _ => null, _ => false));
    }

    [Fact]
    public void Settings_From_Before_The_Disk_Was_Recorded_Copy_Only_Into_A_Folder_Already_There()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Sub("archive"));

        Assert.Null(ArchiveDisk.Refusal(temp.Sub("archive"), NoDisk, _ => 9, _ => false));
        Assert.Contains("introuvable", ArchiveDisk.Refusal(temp.Sub("elsewhere"), NoDisk, _ => 9, _ => false));
        Assert.False(Directory.Exists(temp.Sub("elsewhere")));
    }

    [Fact]
    public void An_Unplugged_Archive_Disk_Is_Said_In_French_Before_Anything_Is_Created()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var absent = "ZYXWVUTSRQPONMLKJIHGFED".First(letter => !used.Contains(letter));

        Assert.Contains("n'est pas branché", ArchiveDisk.Unplugged($@"{absent}:\Sauvegardes"));
        Assert.Null(ArchiveDisk.IdentityOf($@"{absent}:\Sauvegardes"));
        Assert.Null(ArchiveDisk.IdentityOf(@"\\serveur\partage\Sauvegardes"));
    }

    [Fact]
    public void The_Disk_Under_A_Local_Folder_Has_An_Identity()
    {
        using var temp = new TempFolder();

        Assert.NotNull(ArchiveDisk.IdentityOf(temp.Path));
        Assert.Equal(PhysicalDisks.VolumeSerial(temp.Path), ArchiveDisk.IdentityOf(temp.Path));
    }

    // ── Two folders, one disk ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Two_Volumes_Share_A_Disk_When_Their_Extents_Name_One()
    {
        // Two letters on one disk (C: and D: of a PC's own drive), two disks, and a volume Windows will not describe.
        Assert.True(PhysicalDisks.ShareADisk(@"Y:\coffre", @"Z:\copie", _ => new[] { 0 }.ToHashSet()));
        Assert.False(PhysicalDisks.ShareADisk(@"Y:\coffre", @"Z:\copie", root => new[] { root[0] == 'Y' ? 0 : 1 }.ToHashSet()));
        Assert.False(PhysicalDisks.ShareADisk(@"Y:\coffre", @"Z:\copie", root => root[0] == 'Y' ? new[] { 0 }.ToHashSet() : null));

        // A volume spanning two disks shares with either.
        Assert.True(PhysicalDisks.ShareADisk(@"Y:\coffre", @"Z:\copie", root => (root[0] == 'Y' ? new[] { 0, 1 } : new[] { 1 }).ToHashSet()));
    }

    [Fact]
    public void The_Disk_Reader_Names_The_Disk_Under_This_Folder()
    {
        using var temp = new TempFolder();
        var root = PhysicalDisks.VolumeRoot(temp.Path);

        Assert.NotNull(root);
        Assert.NotEmpty(PhysicalDisks.DisksOf(root!) ?? Array.Empty<int>().ToHashSet());
    }

    // ── The coffre's copy, onto the PC's own disk ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_Coffre_Is_Still_Copied_Onto_Its_Own_Disk_And_The_Result_Says_So()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Sub("coffre", "patient"));
        File.WriteAllText(temp.Sub("coffre", "patient", "etude.dcm"), "dicom");
        var settings = new ArchiveCopySettings
        {
            Folder = temp.Sub("archive"),
            GrantSecret = "cle",
            VaultFolder = temp.Sub("coffre"),
        };

        // Nothing answers there: the report is « not reported », the copy is what this test is about.
        var server = new ServerConfig { Host = "127.0.0.1", Port = 1, PortIsExplicit = true };
        var outcome = await new VaultCopyService(server, settings).CopyNowAsync(reuseToken: "jeton");

        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(temp.Sub("archive", VaultCopyService.DestinationFolderName, "patient", "etude.dcm")));
        Assert.Contains("même disque", outcome.Message);
    }
}
