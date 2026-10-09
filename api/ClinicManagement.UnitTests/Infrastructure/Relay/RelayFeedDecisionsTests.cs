using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// The PC de secours's rules (<c>clinic-pc-copy</c> D6b, D12, D25, AC-1.9), as pure functions. The ones that can lose
/// records — following a cloud that went back — come first.
/// </summary>
public class RelayFeedDecisionsTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private static RelayFollowerState Following(string epoch = "e1", long seq = 40) =>
        new() { Epoch = epoch, AppliedSeq = seq, HeadFingerprint = "h" + seq, RowsSeededAtUtc = T0 };

    private static RelayFeedBatch Batch(string epoch = "e1", long high = 45, RelayFeedOutcome outcome = RelayFeedOutcome.Ok) =>
        new(epoch, 40, high, "h" + high, outcome, Array.Empty<RelayRow>());

    // [D12] A changed epoch is another history — a restore, a PITR, a new cluster — and is never followed.
    [Fact]
    public void A_Batch_From_Another_Epoch_Stops_The_Copy()
    {
        Assert.Equal(RelayBatchVerdict.Stop, RelayFeedDecisions.Classify(Following(), Batch(epoch: "e2")));
    }

    // [D12] A cloud whose high-water is below what this PC applied has lost changes this PC holds.
    [Fact]
    public void A_Cloud_Behind_This_Copy_Stops_It()
    {
        Assert.Equal(RelayBatchVerdict.Stop, RelayFeedDecisions.Classify(Following(seq: 40), Batch(high: 39)));
    }

    // [D12] The continuity fingerprint is checked by the cloud; its verdict is obeyed.
    [Fact]
    public void The_Clouds_Own_Went_Back_Verdict_Stops_The_Copy()
    {
        Assert.Equal(RelayBatchVerdict.Stop,
            RelayFeedDecisions.Classify(Following(), Batch(outcome: RelayFeedOutcome.WentBack)));
    }

    [Fact]
    public void A_Backlog_Too_Long_For_One_Batch_Is_A_Reseed()
    {
        Assert.Equal(RelayBatchVerdict.Reseed,
            RelayFeedDecisions.Classify(Following(), Batch(outcome: RelayFeedOutcome.ReseedRequired)));
    }

    [Fact]
    public void An_Ordinary_Batch_Applies()
    {
        Assert.Equal(RelayBatchVerdict.Apply, RelayFeedDecisions.Classify(Following(), Batch()));
    }

    // Before the first copy there is no history to compare: whatever cloud this PC was paired with is the one.
    [Fact]
    public void A_Copy_With_No_Epoch_Yet_Accepts_The_First_One()
    {
        Assert.False(RelayFeedDecisions.WentBack(new RelayFollowerState(), "e9", 0));
    }

    [Fact]
    public void Applying_A_Batch_Moves_The_Position_And_Clears_The_Error()
    {
        var state = Following() with { LastError = "x" };

        var next = RelayFeedDecisions.Applied(state, Batch(high: 45));

        Assert.Equal(45, next.AppliedSeq);
        Assert.Equal("h45", next.HeadFingerprint);
        Assert.Null(next.LastError);
    }

    // An empty batch carries no head of its own: the fingerprint the PC holds stays the one to send next.
    [Fact]
    public void A_Batch_With_No_Head_Keeps_The_Fingerprint()
    {
        var next = RelayFeedDecisions.Applied(Following(), new RelayFeedBatch("e1", 40, 40, null, RelayFeedOutcome.Ok, Array.Empty<RelayRow>()));

        Assert.Equal("h40", next.HeadFingerprint);
    }

    [Fact]
    public void A_Whole_Copy_Sets_The_Position_And_Keeps_The_First_Seed_Instant()
    {
        var state = Following() with { ReseedNeeded = true, RetrySeedAfterUtc = T0.AddMinutes(5) };

        var next = RelayFeedDecisions.Seeded(state, new RelaySnapshotHeader("e1", 90, "h90"), T0.AddHours(3));

        Assert.Equal(90, next.AppliedSeq);
        Assert.Equal("h90", next.HeadFingerprint);
        Assert.False(next.ReseedNeeded);
        Assert.Null(next.RetrySeedAfterUtc);
        Assert.Equal(T0, next.RowsSeededAtUtc);
    }

    // [AC-1.9] Rows are a tenth of the first copy, files the rest.
    [Theory]
    [InlineData(false, 0, 0, 0)]
    [InlineData(true, 0, 0, 100)]
    [InlineData(true, 10, 0, 10)]
    [InlineData(true, 10, 5, 55)]
    [InlineData(true, 10, 10, 100)]
    public void The_Seed_Percentage_Weighs_Files_Over_Rows(bool rowsSeeded, int total, int copied, int expected)
    {
        var state = new RelayFollowerState
        {
            RowsSeededAtUtc = rowsSeeded ? T0 : null, FilesTotal = total, FilesCopied = copied,
        };

        Assert.Equal(expected, RelayFeedDecisions.SeedPercent(state));
        Assert.Equal(expected == 100, RelayFeedDecisions.SeedComplete(state));
    }

    // [D25] The hourly check runs only on a complete, caught-up copy — otherwise every table would differ.
    [Fact]
    public void The_Hourly_Check_Waits_For_A_Complete_Caught_Up_Copy()
    {
        var complete = Following(seq: 40);

        Assert.True(RelayFeedDecisions.DigestDue(complete, cloudHighWater: 40, T0));
        Assert.False(RelayFeedDecisions.DigestDue(complete, cloudHighWater: 41, T0));
        Assert.False(RelayFeedDecisions.DigestDue(complete with { FilesTotal = 3, FilesCopied = 1 }, 40, T0));
        Assert.False(RelayFeedDecisions.DigestDue(complete with { LastDigestAtUtc = T0 }, 40, T0.AddMinutes(59)));
        Assert.True(RelayFeedDecisions.DigestDue(complete with { LastDigestAtUtc = T0 }, 40, T0.AddHours(1)));
    }

    [Fact]
    public void A_Table_Differs_On_Count_Hash_Or_Absence()
    {
        var cloud = new[]
        {
            new RelayTableDigest("Patient", 3, "a"), new RelayTableDigest("Invoice", 2, "b"),
            new RelayTableDigest("Doctor", 1, "c"), new RelayTableDigest("Clinic", 1, "d"),
        };
        var local = new[]
        {
            new RelayTableDigest("Patient", 2, "a"), new RelayTableDigest("Invoice", 2, "x"),
            new RelayTableDigest("Clinic", 1, "d"), new RelayTableDigest("Expense", 4, "e"),
        };

        Assert.Equal(new[] { "Doctor", "Expense", "Invoice", "Patient" }, RelayFeedDecisions.Mismatched(cloud, local));
        Assert.Empty(RelayFeedDecisions.Mismatched(cloud, cloud));
    }

    [Fact]
    public void The_Snapshot_Header_Is_Read_From_Its_Opening_Fields()
    {
        var json = Encoding.UTF8.GetBytes("{\"epoch\":\"7-1-16384\",\"highWater\":120,\"head\":\"ABC\",\"tables\":[{\"table\":\"Clinic\",\"rows\":[]}]}");

        Assert.Equal(new RelaySnapshotHeader("7-1-16384", 120, "ABC"), RelaySnapshotHeaderReader.Parse(json, isFinalBlock: true));
    }

    [Fact]
    public void A_Snapshot_With_No_Head_Yet_Reads_A_Null_Head()
    {
        var json = Encoding.UTF8.GetBytes("{\"epoch\":\"e\",\"highWater\":0,\"head\":null,\"tables\":[]}");

        Assert.Null(RelaySnapshotHeaderReader.Parse(json, isFinalBlock: true).Head);
    }

    // A snapshot that does not say which history it is would be followed blindly — it is refused instead.
    [Fact]
    public void A_Snapshot_That_Does_Not_Name_Its_History_Is_Refused()
    {
        var json = Encoding.UTF8.GetBytes("{\"highWater\":3,\"tables\":[]}");

        Assert.Throws<InvalidDataException>(() => RelaySnapshotHeaderReader.Parse(json, isFinalBlock: true));
    }

    // The header is read from the first block alone, however large the tables behind it.
    [Fact]
    public async Task The_Header_Is_Read_Without_Reading_The_Tables()
    {
        var big = "{\"epoch\":\"e\",\"highWater\":5,\"head\":\"h\",\"tables\":[{\"table\":\"Patient\",\"rows\":["
                  + string.Join(",", Enumerable.Repeat("{\"Id\":\"x\"}", 50_000)) + "]}]}";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(big));

        var header = await RelaySnapshotHeaderReader.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(5, header.HighWater);
        Assert.True(stream.Position < stream.Length);
    }
}
