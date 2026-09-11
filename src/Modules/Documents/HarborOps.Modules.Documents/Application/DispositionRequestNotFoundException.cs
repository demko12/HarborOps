namespace HarborOps.Modules.Documents.Application;

internal sealed class DispositionRequestNotFoundException(Guid requestId)
    : Exception($"Disposition request '{requestId}' was not found.")
{
    internal Guid RequestId { get; } = requestId;
}
