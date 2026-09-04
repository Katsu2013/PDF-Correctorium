using System.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>指定バイト数を超える書込みを、その時点で拒否するストリームです。</summary>
internal sealed class LengthLimitedWriteStream(Stream inner, long maximumBytes) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position
    {
        get => inner.Position;
        set
        {
            if (value < 0 || value > maximumBytes) throw new InvalidDataException("出力位置が許可サイズを超えています。");
            inner.Position = value;
        }
    }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = inner.Seek(offset, origin);
        if (position < 0 || position > maximumBytes)
            throw new InvalidDataException("出力位置が許可サイズを超えています。");
        return position;
    }
    public override void SetLength(long value)
    {
        if (value < 0 || value > maximumBytes)
            throw new InvalidDataException("出力長が許可サイズを超えています。");
        inner.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(count);
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(buffer.Length);
        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacity(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        EnsureCapacity(count);
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    private void EnsureCapacity(int count)
    {
        if (maximumBytes <= 0 || count < 0 || inner.Position > maximumBytes - count)
            throw new InvalidDataException($"PDF処理ワーカーの出力が上限 {maximumBytes:N0} バイトを超えました。");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
