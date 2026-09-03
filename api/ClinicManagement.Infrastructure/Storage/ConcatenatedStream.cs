namespace ClinicManagement.Infrastructure.Storage;

/// <summary>
/// Several streams read end to end as one, opened lazily and disposed as each is exhausted.
///
/// <para>⚠️ <b>This is what keeps assembling a resumable upload free of memory.</b> The parts have to become one
/// blob, and the object store offers no server-side concatenation — so the choice is to buffer the whole file
/// (a gigabyte of the server's RAM, per concurrent upload) or to hand the store a stream that produces the parts
/// in order. Only one part is open at a time.</para>
///
/// <para>⚠️ <b>The factories are deferred on purpose.</b> Opening all of them up front would hold one HTTP
/// response or one file handle per part for the whole assembly — a hundred-part upload is a hundred open
/// connections to the object store, most of them idle.</para>
/// </summary>
internal sealed class ConcatenatedStream : Stream
{
    private readonly IReadOnlyList<Func<CancellationToken, Task<Stream>>> _parts;
    private readonly long _length;

    private int _index;
    private Stream? _current;

    public ConcatenatedStream(IReadOnlyList<Func<CancellationToken, Task<Stream>>> parts, long length)
    {
        _parts = parts;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    /// <summary>
    /// The total the caller declared. ⚠️ Reported even though the stream is not seekable, because the object
    /// store's upload needs a size up front — and it is the row's own arithmetic, not a guess.
    /// </summary>
    public override long Length => _length;

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_current == null)
            {
                if (_index >= _parts.Count)
                {
                    return 0;
                }

                _current = await _parts[_index++](cancellationToken);
            }

            var read = await _current.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                return read;
            }

            // This part is spent; the next loop opens the next one, and the last one ends the stream.
            await _current.DisposeAsync();
            _current = null;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _current?.Dispose();
            _current = null;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_current != null)
        {
            await _current.DisposeAsync();
            _current = null;
        }

        await base.DisposeAsync();
    }
}
