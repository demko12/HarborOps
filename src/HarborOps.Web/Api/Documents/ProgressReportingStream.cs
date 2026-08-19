namespace HarborOps.Web.Api.Documents;

/// <summary>
/// Read-only wrapper around an upload source stream that reports cumulative read progress
/// as a percentage of <paramref name="totalLength"/>. Only the read path is supported -
/// this exists solely to be handed to <see cref="System.Net.Http.StreamContent"/> for an
/// outgoing upload, never for writing.
/// </summary>
internal sealed class ProgressReportingStream(Stream inner, long totalLength, IProgress<double>? progress) : Stream
{
    private long bytesRead;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException("ProgressReportingStream is forward-only.");
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        ReportProgress(read);

        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer, offset, count, cancellationToken);
        ReportProgress(read);

        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        ReportProgress(read);

        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("ProgressReportingStream is forward-only.");

    public override void SetLength(long value) =>
        throw new NotSupportedException("ProgressReportingStream is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("ProgressReportingStream is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ReportProgress(int read)
    {
        if (read <= 0 || progress is null)
        {
            return;
        }

        bytesRead += read;
        var percentage = totalLength > 0 ? (double)bytesRead / totalLength * 100 : 100;
        progress.Report(Math.Min(percentage, 100));
    }
}
