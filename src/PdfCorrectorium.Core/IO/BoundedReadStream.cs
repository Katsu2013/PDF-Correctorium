namespace PdfCorrectorium.Core.IO;

/// <summary>Limits actual reads, independently of a file or ZIP header's declared size.</summary>
public sealed class BoundedReadStream : Stream
{
    private readonly Stream _input;
    private readonly long _maximumBytes;
    private readonly long? _expectedBytes;

    public BoundedReadStream(Stream input, long maximumBytes, long? expectedBytes = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (expectedBytes < 0 || expectedBytes > maximumBytes)
            throw new ArgumentOutOfRangeException(nameof(expectedBytes));
        _input = input;
        _maximumBytes = expectedBytes ?? maximumBytes;
        _expectedBytes = expectedBytes;
    }

    public long BytesRead { get; private set; }
    public override bool CanRead => _input.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int ReadSize(int requested)
    {
        var remaining = _maximumBytes - BytesRead;
        return requested <= remaining ? requested : (int)remaining + 1;
    }

    private int RecordRead(int read)
    {
        if (read > _maximumBytes - BytesRead)
            throw new InvalidDataException("The input exceeds the allowed byte count or declared entry size.");
        BytesRead += read;
        if (read == 0 && _expectedBytes is long expected && BytesRead != expected)
            throw new InvalidDataException("The input ends before its declared entry size.");
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) => buffer.IsEmpty ? 0 : RecordRead(_input.Read(buffer[..ReadSize(buffer.Length)]));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty) return 0;
        return RecordRead(await _input.ReadAsync(buffer[..ReadSize(buffer.Length)], cancellationToken).ConfigureAwait(false));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _input.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _input.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
