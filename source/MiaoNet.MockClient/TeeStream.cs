namespace MiaoNet.MockClient;

public class TeeStream : Stream
{
    private readonly Stream _primary;
    private readonly Stream _sink;
    private readonly bool _leaveOpen;

    public TeeStream(Stream primary, Stream sink, bool leaveOpen = false)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => _primary.CanRead && _sink.CanWrite;
    public override bool CanSeek => _primary.CanSeek;
    public override bool CanWrite => _primary.CanWrite;

    public override long Length => _primary.Length;

    public override long Position
    {
        get => _primary.Position;
        set => _primary.Position = value;
    }

    public override void Flush()
    {
        _primary.Flush();
        _sink.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _primary.FlushAsync(cancellationToken);
        await _sink.FlushAsync(cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
        => _primary.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _primary.WriteAsync(buffer, offset, count, cancellationToken);

    public override void WriteByte(byte value)
        => _primary.WriteByte(value);

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _primary.Read(buffer, offset, count);
        if (read > 0)
            _sink.Write(buffer, offset, read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _primary.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if (read > 0)
            await _sink.WriteAsync(buffer.AsMemory(offset, read), cancellationToken).ConfigureAwait(false);
        return read;
    }

    public override int ReadByte()
    {
        int b = _primary.ReadByte();
        if (b >= 0)
            _sink.WriteByte((byte)b);
        return b;
    }

    public override long Seek(long offset, SeekOrigin origin)
        => _primary.Seek(offset, origin);

    public override void SetLength(long value)
        => _primary.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _primary.Dispose();
            _sink.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _primary.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
            await _sink.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        return read;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
        => _primary.Write(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _primary.WriteAsync(buffer, cancellationToken);
}