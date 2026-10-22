using ClinicManagement.Infrastructure.Relay;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Relay;

/// <summary>
/// Review finding 6 (2026-10-13): a download read with nothing but the service's own token waited for ever across a cut —
/// HttpClient's timeout ends at the headers — and the copy tick waiting on it was the only thing that drove the return.
/// </summary>
public class RelayTransferStallTests
{
    /// <summary>A body that sent its first bytes, then nothing more: the line died with the read open.</summary>
    private sealed class DeadAfterFirstChunk : Stream
    {
        private bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                buffer.Span[0] = 42;
                return 1;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_Body_That_Stops_Sending_Ends_The_Read_As_No_Answer()
    {
        using var target = new MemoryStream();

        var stalled = await Assert.ThrowsAsync<IOException>(() =>
            RelayCloudClient.CopyUntilStalledAsync(new DeadAfterFirstChunk(), target, TimeSpan.FromMilliseconds(200), CancellationToken.None));

        Assert.Contains("n'envoie plus rien", stalled.Message);
        Assert.Equal(1, target.Length);
    }

    [Fact]
    public async Task A_Body_That_Keeps_Coming_Is_Copied_Whole()
    {
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
        using var target = new MemoryStream();

        await RelayCloudClient.CopyUntilStalledAsync(new MemoryStream(bytes), target, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(bytes, target.ToArray());
    }

    // The service stopping is not a dead line: its own cancellation still ends the read as a cancellation.
    [Fact]
    public async Task The_Services_Own_Stop_Is_Not_Read_As_A_Dead_Line()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var target = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RelayCloudClient.CopyUntilStalledAsync(new DeadAfterFirstChunk(), target, TimeSpan.FromSeconds(30), stop.Token));
    }
}
