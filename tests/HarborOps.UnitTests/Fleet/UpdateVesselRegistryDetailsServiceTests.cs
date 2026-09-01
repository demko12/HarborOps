using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class UpdateVesselRegistryDetailsServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 31, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateAsync_rejects_invalid_request_before_touching_the_database()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<UpdateVesselRegistryDetailsValidationException>(
            () => service.UpdateAsync(
                Guid.NewGuid(),
                new UpdateVesselRegistryDetailsRequest { VesselTypeId = null, Name = " ", FlagState = " " },
                CancellationToken.None));

        Assert.Contains(UpdateVesselRegistryDetailsValidator.VesselTypeIdField, exception.Errors);
        Assert.Contains(UpdateVesselRegistryDetailsValidator.NameField, exception.Errors);
        Assert.Contains(UpdateVesselRegistryDetailsValidator.FlagStateField, exception.Errors);
    }

    [Fact]
    public async Task UpdateAsync_rejects_malformed_identifiers()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<UpdateVesselRegistryDetailsValidationException>(
            () => service.UpdateAsync(
                vessel.Id,
                Request(vesselTypeId) with { Mmsi = "25X12", CallSign = "9HA-12" },
                CancellationToken.None));

        Assert.Contains(UpdateVesselRegistryDetailsValidator.MmsiField, exception.Errors);
        Assert.Contains(UpdateVesselRegistryDetailsValidator.CallSignField, exception.Errors);
    }

    [Fact]
    public async Task UpdateAsync_throws_not_found_for_unknown_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var service = CreateService(dbContext);
        var vesselId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VesselNotFoundException>(
            () => service.UpdateAsync(vesselId, Request(vesselTypeId), CancellationToken.None));

        Assert.Equal(vesselId, exception.VesselId);
    }

    [Fact]
    public async Task UpdateAsync_throws_not_found_for_unknown_vessel_type()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        var service = CreateService(dbContext);
        var unknownType = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VesselTypeNotFoundException>(
            () => service.UpdateAsync(vessel.Id, Request(unknownType), CancellationToken.None));

        Assert.Equal(unknownType, exception.VesselTypeId);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_normalized_mmsi_already_used_by_another_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var edited = await SeedVesselAsync(dbContext, vesselTypeId, imoNumber: "1000001");
        var other = await SeedVesselAsync(dbContext, vesselTypeId, imoNumber: "1000002");
        await ApplyRegistryAsync(dbContext, other.Id, vesselTypeId, mmsi: "256123000");
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DuplicateMmsiException>(
            () => service.UpdateAsync(
                edited.Id,
                Request(vesselTypeId) with { Mmsi = " 256123000 " },
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_call_sign_already_used_regardless_of_casing()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var edited = await SeedVesselAsync(dbContext, vesselTypeId, imoNumber: "1000001");
        var other = await SeedVesselAsync(dbContext, vesselTypeId, imoNumber: "1000002");
        await ApplyRegistryAsync(dbContext, other.Id, vesselTypeId, callSign: "9HA1234");
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DuplicateCallSignException>(
            () => service.UpdateAsync(
                edited.Id,
                Request(vesselTypeId) with { CallSign = "9ha1234" },
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_allows_a_vessel_to_keep_its_own_identifiers()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        await ApplyRegistryAsync(dbContext, vessel.Id, vesselTypeId, mmsi: "256123000", callSign: "9HA1234");
        var service = CreateService(dbContext);

        var response = await service.UpdateAsync(
            vessel.Id,
            Request(vesselTypeId) with { Mmsi = "256123000", CallSign = "9HA1234", Name = "Renamed" },
            CancellationToken.None);

        Assert.Equal("Renamed", response.Name);
    }

    [Fact]
    public async Task UpdateAsync_updates_mutable_fields_and_leaves_imo_and_company_untouched()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var replacementTypeId = await SeedVesselTypeAsync(dbContext, "Chemical Tanker", "TANK");
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, imoNumber: "9111111");
        var originalCompanyId = vessel.CompanyId;
        var service = CreateService(dbContext);

        var response = await service.UpdateAsync(
            vessel.Id,
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = replacementTypeId,
                Name = "  Northern Star  ",
                Mmsi = "  256123000  ",
                CallSign = "  9HA1234  ",
                FlagState = "  Montenegro  ",
                PortOfRegistry = "  Bar  ",
                ClassificationSociety = "  Lloyd's Register  ",
                LifecycleStage = "  In service  ",
                MsmdId = "  MSMD-42  ",
            },
            CancellationToken.None);

        var persisted = await dbContext.Set<Vessel>().AsNoTracking().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal(replacementTypeId, persisted.VesselTypeId);
        Assert.Equal("Northern Star", persisted.Name);
        Assert.Equal("256123000", persisted.Mmsi);
        Assert.Equal("9HA1234", persisted.CallSign);
        Assert.Equal("Montenegro", persisted.FlagState);
        Assert.Equal("Bar", persisted.PortOfRegistry);
        Assert.Equal("Lloyd's Register", persisted.ClassificationSociety);
        Assert.Equal("In service", persisted.LifecycleStage);
        Assert.Equal("MSMD-42", persisted.MsmdId);
        Assert.Equal("9111111", persisted.ImoNumber);
        Assert.Equal(originalCompanyId, persisted.CompanyId);
        Assert.Equal(FixedNow.UtcDateTime, persisted.UpdatedAtUtc);
        Assert.Equal("Northern Star", response.Name);
        Assert.Equal("9111111", response.ImoNumber);
    }

    [Fact]
    public async Task UpdateAsync_treats_blank_optional_fields_as_cleared()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        await ApplyRegistryAsync(dbContext, vessel.Id, vesselTypeId, mmsi: "256123000", portOfRegistry: "Bar");
        var service = CreateService(dbContext);

        await service.UpdateAsync(
            vessel.Id,
            Request(vesselTypeId) with { Mmsi = "   ", PortOfRegistry = "" },
            CancellationToken.None);

        var persisted = await dbContext.Set<Vessel>().AsNoTracking().SingleAsync(v => v.Id == vessel.Id);

        Assert.Null(persisted.Mmsi);
        Assert.Null(persisted.PortOfRegistry);
    }

    [Fact]
    public async Task UpdateAsync_does_not_advance_the_timestamp_for_an_unchanged_request()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId);
        await ApplyRegistryAsync(dbContext, vessel.Id, vesselTypeId, mmsi: "256123000");
        var unchangedUpdatedAt = (await dbContext.Set<Vessel>().AsNoTracking()
            .SingleAsync(v => v.Id == vessel.Id)).UpdatedAtUtc;
        var service = CreateService(dbContext);

        var response = await service.UpdateAsync(
            vessel.Id,
            Request(vesselTypeId) with { Mmsi = "256123000" },
            CancellationToken.None);

        var persisted = await dbContext.Set<Vessel>().AsNoTracking().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal(unchangedUpdatedAt, persisted.UpdatedAtUtc);
        Assert.NotEqual(FixedNow.UtcDateTime, persisted.UpdatedAtUtc);
        Assert.Equal(vessel.Id, response.VesselId);
    }

    private static UpdateVesselRegistryDetailsRequest Request(Guid vesselTypeId) => new()
    {
        VesselTypeId = vesselTypeId,
        Name = "North Star",
        FlagState = "Malta",
    };

    private static async Task ApplyRegistryAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        Guid vesselTypeId,
        string? mmsi = null,
        string? callSign = null,
        string? portOfRegistry = null)
    {
        var vessel = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vesselId);
        vessel.UpdateRegistryDetails(
            vesselTypeId,
            vessel.Name,
            mmsi,
            callSign,
            vessel.FlagState,
            portOfRegistry,
            null,
            null,
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static UpdateVesselRegistryDetailsService CreateService(HarborOpsDbContext dbContext) =>
        new(
            dbContext,
            new FixedTimeProvider(FixedNow),
            new GetVesselDetailsQuery(dbContext));

    private static async Task<Guid> SeedVesselTypeAsync(
        HarborOpsDbContext dbContext,
        string name = "Container Ship",
        string code = "CNTR")
    {
        var vesselType = VesselType.Register(name, code, $"{name} vessel");
        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return vesselType.Id;
    }

    private static async Task<Vessel> SeedVesselAsync(
        HarborOpsDbContext dbContext,
        Guid vesselTypeId,
        string imoNumber = "1234567")
    {
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, "North Star", imoNumber, "Malta", "Active");
        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return vessel;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
