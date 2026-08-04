namespace HarborOps.Modules.Fleet.Application;

internal sealed class VesselTypeNotFoundException(Guid vesselTypeId)
    : Exception($"Vessel type '{vesselTypeId}' was not found.")
{
    internal Guid VesselTypeId { get; } = vesselTypeId;
}
