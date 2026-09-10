using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.UnitTests.Operations;

internal sealed class FakeVesselLookupService(params VesselLookupItem[] vessels) : IVesselLookupService
{
    internal int CallCount { get; private set; }

    internal VesselLookupRequest? LastRequest { get; private set; }

    internal CancellationToken LastCancellationToken { get; private set; }

    public Task<VesselLookupResponse> LookupAsync(VesselLookupRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastCancellationToken = cancellationToken;

        var requestedVesselIds = request.VesselIds ?? [];
        var matches = vessels.Where(vessel => requestedVesselIds.Contains(vessel.Id)).ToArray();

        return Task.FromResult(new VesselLookupResponse(matches));
    }
}
