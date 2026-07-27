using ClinicManagement.Infrastructure.Security;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Security;

/// <summary>
/// Whether a backup destination can be protected at all (security-hardening US-14 / AC-14.3).
///
/// The admin picks where a backup lands. On a local fixed disk we lock it down like the live data; on a USB
/// stick (often FAT32 — no ACLs whatsoever) or a network share (the server's ACL to enforce, not ours) a
/// "secured" folder would be a false promise, so the backup proceeds and the admin is told plainly. Refusing
/// outright would be worse: an operator who cannot back up to a USB drive stops backing up.
/// </summary>
public class BackupProtectionPolicyTests
{
    [Fact]
    public void A_local_fixed_disk_can_be_protected()
    {
        Assert.True(BackupProtectionPolicy.CanProtect(DriveType.Fixed));
    }

    [Theory]
    [InlineData(DriveType.Removable)] // USB stick — frequently FAT32, no ACL support at all
    [InlineData(DriveType.Network)]   // share — the ACL is the far end's to enforce
    [InlineData(DriveType.CDRom)]
    [InlineData(DriveType.Ram)]
    [InlineData(DriveType.NoRootDirectory)]
    [InlineData(DriveType.Unknown)]   // unresolvable ⇒ warn rather than claim an unverified protection
    public void Anything_else_cannot_be_protected(DriveType driveType)
    {
        Assert.False(BackupProtectionPolicy.CanProtect(driveType));
    }

    [Fact]
    public void A_unc_path_is_treated_as_a_network_destination() // no DriveInfo exists for \\server\share
    {
        Assert.Equal(DriveType.Network, BackupProtectionPolicy.ResolveDriveType(@"\\server\share\backups"));
    }

    [Fact]
    public void The_temp_directory_resolves_to_a_real_drive_type() // sanity: resolution actually works
    {
        var driveType = BackupProtectionPolicy.ResolveDriveType(Path.GetTempPath());

        Assert.NotEqual(DriveType.NoRootDirectory, driveType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("::invalid::")]
    public void An_unresolvable_path_reads_as_unknown_and_therefore_unprotectable(string path)
    {
        var driveType = BackupProtectionPolicy.ResolveDriveType(path);

        Assert.False(BackupProtectionPolicy.CanProtect(driveType));
    }

    [Fact]
    public void The_warning_names_what_is_actually_at_risk() // not a vague "could not set permissions"
    {
        var warning = BackupProtectionPolicy.UnprotectableDestinationWarning;

        Assert.Contains("dossiers patients", warning, StringComparison.OrdinalIgnoreCase);
        // It must also be clear the backup DID succeed, so the admin does not retry in a loop.
        Assert.Contains("créée", warning, StringComparison.OrdinalIgnoreCase);
    }
}
