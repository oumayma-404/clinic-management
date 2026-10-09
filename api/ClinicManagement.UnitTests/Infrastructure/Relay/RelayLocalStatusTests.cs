using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>What a PC de secours knows about itself (<c>clinic-pc-copy</c> AC-8.1): read from its own position file.</summary>
public sealed class RelayLocalStatusTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-local-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = T0;

    private RelayLocalStatus Status(bool isRelay = true) =>
        new(isRelay, new RelayFollowerStateStore(_dir), () => _now);

    [Fact]
    public void A_Released_Pc_Reads_As_Retired_With_Its_Date()
    {
        new RelayFollowerStateStore(_dir).Save(new RelayFollowerState { Released = true, ReleasedAtUtc = T0.AddDays(-1) });

        var status = Status();

        Assert.True(status.IsRetired);
        Assert.Equal(T0.AddDays(-1), status.RetiredAtUtc);
    }

    [Fact]
    public void A_Following_Pc_Is_Not_Retired()
    {
        new RelayFollowerStateStore(_dir).Save(new RelayFollowerState { AppliedSeq = 3 });

        Assert.False(Status().IsRetired);
        Assert.Null(Status().RetiredAtUtc);
    }

    // Off a PC de secours the answer is « no » without reading anything — a released file left on a LAN server
    // must not lock its staff out.
    [Fact]
    public void Off_A_Pc_De_Secours_Nothing_Is_Retired()
    {
        new RelayFollowerStateStore(_dir).Save(new RelayFollowerState { Released = true });

        Assert.False(Status(isRelay: false).IsRetired);
    }

    // Asked on every request: re-read every few seconds, not on every call, and not never.
    [Fact]
    public void The_File_Is_Re_Read_After_A_Few_Seconds()
    {
        var store = new RelayFollowerStateStore(_dir);
        store.Save(new RelayFollowerState());
        var status = Status();
        Assert.False(status.IsRetired);

        store.Save(new RelayFollowerState { Released = true, ReleasedAtUtc = T0 });
        Assert.False(status.IsRetired);

        _now = T0.AddSeconds(6);
        Assert.True(status.IsRetired);
    }

    // [D20] The cause is said only while this PC holds — a cause left in the file after the cut means nothing.
    [Fact]
    public void The_Cut_Cause_Is_Read_Only_While_Holding()
    {
        var lease = new RelayLease(_dir, () => _now);
        lease.RecordCutCause(RelayCutCauses.Cloud);
        var status = new RelayLocalStatus(true, new RelayFollowerStateStore(_dir), () => _now, lease);
        Assert.Null(status.CutCause);

        // A new takeover starts with no cause: the last cut's is not this one's.
        lease.TakeOver();
        Assert.Null(status.CutCause);

        lease.RecordCutCause(RelayCutCauses.Cloud);
        Assert.Equal(RelayCutCauses.Cloud, status.CutCause);

        Assert.Null(new RelayLocalStatus(false, new RelayFollowerStateStore(_dir), () => _now, lease).CutCause);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
