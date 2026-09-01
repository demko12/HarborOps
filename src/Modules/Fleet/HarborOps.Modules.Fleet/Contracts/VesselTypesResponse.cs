namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselTypesResponse(IReadOnlyList<VesselTypeListItem> VesselTypes);
