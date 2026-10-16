using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>The PC de secours's position on disk (<c>clinic-pc-copy</c> D12): kept beside the credentials, never in the copy.</summary>
public sealed class RelayFollowerStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-state-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_Saved_Position_Loads_Back()
    {
        var store = new RelayFollowerStateStore(_dir);
        var state = new RelayFollowerState
        {
            Epoch = "e1", AppliedSeq = 42, HeadFingerprint = "h42",
            RowsSeededAtUtc = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc), MismatchTables = new[] { "Patient" },
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(42, loaded.AppliedSeq);
        Assert.Equal("e1", loaded.Epoch);
        Assert.Equal("h42", loaded.HeadFingerprint);
        Assert.Equal(new[] { "Patient" }, loaded.MismatchTables);
        Assert.True(loaded.RowsSeeded);
    }

    [Fact]
    public void No_File_Is_A_Fresh_Start()
    {
        var state = new RelayFollowerStateStore(_dir).Load();

        Assert.False(state.RowsSeeded);
        Assert.Null(state.Epoch);
        Assert.Null(state.StoppedReason);
    }

    // ⚠️ An unreadable file is not a fresh start: that would re-seed with no epoch to compare and follow any cloud.
    [Fact]
    public void An_Unreadable_File_Stops_The_Copy_Rather_Than_Starting_Over()
    {
        var store = new RelayFollowerStateStore(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.NotNull(store.Load().StoppedReason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
