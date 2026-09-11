namespace HarborOps.Modules.Cargo.Domain;

internal sealed class BillOfLadingVoidReasonRequiredException(Guid billOfLadingId)
    : Exception($"Bill of lading '{billOfLadingId}' cannot be voided without a reason.")
{
    internal Guid BillOfLadingId { get; } = billOfLadingId;
}
