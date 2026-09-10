using HarborOps.Api.Composition.Maintenance;
using HarborOps.Modules.Fleet.Contracts;

namespace HarborOps.UnitTests.Operations;

public sealed class FleetVesselExistenceLookupTests
{
    [Fact]
    public async Task GetAsync_returns_a_result_when_the_lookup_finds_exactly_one_vessel()
    {
        var vesselId = Guid.NewGuid();
        var lookupService = new FakeVesselLookupService(new VesselLookupItem(vesselId, "MV Example"));
        var adapter = new FleetVesselExistenceLookup(lookupService);

        var result = await adapter.GetAsync(vesselId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(vesselId, result.VesselId);
        Assert.Equal("MV Example", result.Name);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_lookup_finds_no_vessel()
    {
        var lookupService = new FakeVesselLookupService();
        var adapter = new FleetVesselExistenceLookup(lookupService);

        var result = await adapter.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_requests_only_the_single_vessel_id_it_was_asked_about()
    {
        var vesselId = Guid.NewGuid();
        var lookupService = new FakeVesselLookupService(new VesselLookupItem(vesselId, "MV Example"));
        var adapter = new FleetVesselExistenceLookup(lookupService);

        await adapter.GetAsync(vesselId, CancellationToken.None);

        Assert.NotNull(lookupService.LastRequest);
        Assert.Equal(1, lookupService.CallCount);
        Assert.Equal([vesselId], lookupService.LastRequest.VesselIds);
    }

    [Fact]
    public async Task GetAsync_forwards_the_cancellation_token_to_the_underlying_lookup()
    {
        var vesselId = Guid.NewGuid();
        var lookupService = new FakeVesselLookupService(new VesselLookupItem(vesselId, "MV Example"));
        var adapter = new FleetVesselExistenceLookup(lookupService);
        using var cancellationTokenSource = new CancellationTokenSource();

        await adapter.GetAsync(vesselId, cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, lookupService.LastCancellationToken);
    }
}
