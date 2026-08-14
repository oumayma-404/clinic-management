using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain.Entities;

/// <summary>
/// The recovery-point ledger row (<c>clinic-recovery-points</c>) — what makes one usable, and what makes one a record
/// of a failure rather than something to restore from.
/// </summary>
public class ClinicRecoveryPointTests
{
    private static readonly Guid Clinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Started = new(2026, 8, 14, 2, 0, 0, DateTimeKind.Utc);

    private static ClinicRecoveryPoint New(
        ClinicArchiveContents contents = ClinicArchiveContents.RowsOnly) =>
        new(Guid.NewGuid(), Clinic, contents, Started);

    // A fresh row is Running and is NOT restorable. That is the state a crash leaves behind, and it is the whole
    // reason the row is committed before the build starts: « rien cette nuit-là » is the reading that loses data.
    [Fact]
    public void A_New_Point_Is_Running_And_Not_Restorable()
    {
        var point = New();

        Assert.Equal(BackupOutcome.Running, point.Outcome);
        Assert.False(point.IsRestorable);
        Assert.Null(point.StorageKey);
        Assert.Null(point.CompletedAt);
    }

    [Fact]
    public void A_Succeeded_Point_Keeps_Its_Key_Its_Counts_And_Is_Restorable()
    {
        var point = New();
        var completed = Started.AddMinutes(3);

        point.MarkSucceeded("clinics/x/recovery-points/2026-08-14-abc.zip", 2048, tableCount: 41, rowCount: 9137, completed);

        Assert.Equal(BackupOutcome.Succeeded, point.Outcome);
        Assert.True(point.IsRestorable);
        Assert.Equal("clinics/x/recovery-points/2026-08-14-abc.zip", point.StorageKey);
        Assert.Equal(2048, point.SizeBytes);
        Assert.Equal(41, point.TableCount);
        Assert.Equal(9137, point.RowCount);
        Assert.Equal(completed, point.CompletedAt);
        Assert.Null(point.Error);
    }

    // ⚠️ A success with no key is unrepresentable rather than merely unlikely: `IsRestorable` is what the restore
    // command asks, so a row that claimed success and named nothing would be offered on the screen and refused on
    // the click — a dead control at the moment somebody has lost data.
    [Fact]
    public void A_Success_Must_Name_Where_It_Landed()
    {
        var point = New();

        Assert.Throws<ArgumentException>(() =>
            point.MarkSucceeded(" ", 1, 1, 1, Started.AddMinutes(1)));

        Assert.Equal(BackupOutcome.Running, point.Outcome);
    }

    [Fact]
    public void A_Failed_Point_Keeps_Its_Reason_And_Is_Not_Restorable()
    {
        var point = New();

        point.MarkFailed("Le stockage des fichiers est injoignable.", Started.AddMinutes(1));

        Assert.Equal(BackupOutcome.Failed, point.Outcome);
        Assert.False(point.IsRestorable);
        Assert.Equal("Le stockage des fichiers est injoignable.", point.Error);
    }

    // A blank reason is replaced rather than stored: this row is read by a dentist, and an empty « Erreur : » line
    // is the one outcome worse than a technical one.
    [Fact]
    public void A_Failure_With_No_Reason_Still_Says_Something()
    {
        var point = New();

        point.MarkFailed("   ", Started.AddMinutes(1));

        Assert.False(string.IsNullOrWhiteSpace(point.Error));
    }

    // The contents are recorded on the row as well as in the manifest, so the list can say « lignes seulement »
    // without opening the file — and so the confirmation can warn before the click.
    [Theory]
    [InlineData(ClinicArchiveContents.RowsOnly)]
    [InlineData(ClinicArchiveContents.RowsAndFiles)]
    public void The_Point_Remembers_What_It_Carries(ClinicArchiveContents contents)
    {
        Assert.Equal(contents, New(contents).Contents);
    }

    // ⚠️ RowsAndFiles must stay the enum's 0: an archive written before the field existed deserialises to the
    // default, and every one of those carried its files. Asserted as a literal, because that is the one thing a
    // derived check cannot see — and getting it wrong would silently relabel every historical archive.
    [Fact]
    public void RowsAndFiles_Is_The_Default_So_Older_Archives_Read_As_Carrying_Them()
    {
        Assert.Equal(0, (int)ClinicArchiveContents.RowsAndFiles);
        Assert.Equal(default, ClinicArchiveContents.RowsAndFiles);
    }
}
