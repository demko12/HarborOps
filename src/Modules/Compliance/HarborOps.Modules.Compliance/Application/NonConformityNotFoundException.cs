namespace HarborOps.Modules.Compliance.Application;

internal sealed class NonConformityNotFoundException(int nonConformityId)
    : Exception($"Non-conformity '{nonConformityId}' was not found.")
{
    internal int NonConformityId { get; } = nonConformityId;
}
