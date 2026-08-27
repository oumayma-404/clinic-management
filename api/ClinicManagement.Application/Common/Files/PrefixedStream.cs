namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// A forward-only read of « the bytes already consumed, then the rest » — the fallback for a source that cannot
/// be rewound, so that inspecting a header never costs the file being buffered whole.
/// </summary>
internal sealed class PrefixedStream : Stream
{
    private readonly ReadOnlyMemory<byte> _prefix;
    private readonly Stream _rest;
    private int _prefixPosition;

    public PrefixedStream(ReadOnlyMemory<byte> prefix, Stream rest)
    {
        _prefix = prefix;
        _rest = rest;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var fromPrefix = TakePrefix(buffer);
        return fromPrefix > 0 ? fromPrefix : _rest.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var fromPrefix = TakePrefix(buffer.Span);
        return fromPrefix > 0 ? fromPrefix : await _rest.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int TakePrefix(Span<byte> buffer)
    {
        var remaining = _prefix.Length - _prefixPosition;
        if (remaining <= 0 || buffer.Length == 0)
        {
            return 0;
        }

        var take = Math.Min(remaining, buffer.Length);
        _prefix.Span.Slice(_prefixPosition, take).CopyTo(buffer);
        _prefixPosition += take;
        return take;
    }
}
