namespace HarborOps.Modules.Documents.Application;

internal sealed class FileTooLargeException(long actualSizeBytes, long maxSizeBytes)
    : Exception($"File size {actualSizeBytes} bytes exceeds the maximum allowed size of {maxSizeBytes} bytes.")
{
    internal long ActualSizeBytes { get; } = actualSizeBytes;
    internal long MaxSizeBytes { get; } = maxSizeBytes;
}
