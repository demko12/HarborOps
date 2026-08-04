using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class RegisterVesselServiceTests
{
    [Fact]
    public async Task Register_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterVesselService(dbContext);
        var request = new RegisterVesselRequest
        {
            CompanyId = null,
            VesselTypeId = null,
            Name = " ",
            ImoNumber = "ABC",
            FlagState = null,
            CurrentStatus = string.Empty,
        };

        var exception = await Assert.ThrowsAsync<RegisterVesselValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(6, exception.Errors.Count);
        Assert.Contains(RegisterVesselValidator.CompanyIdField, exception.Errors);
        Assert.Contains(RegisterVesselValidator.VesselTypeIdField, exception.Errors);
        Assert.Contains(RegisterVesselValidator.NameField, exception.Errors);
        Assert.Contains(RegisterVesselValidator.ImoNumberField, exception.Errors);
        Assert.Contains(RegisterVesselValidator.FlagStateField, exception.Errors);
        Assert.Contains(RegisterVesselValidator.CurrentStatusField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_duplicate_imo_number()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var service = new RegisterVesselService(dbContext);
        var firstRequest = CreateValidRequest("7654321", vesselTypeId);
        var duplicateRequest = firstRequest with { Name = "Duplicate vessel" };

        await service.RegisterAsync(firstRequest, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateImoNumberException>(
            () => service.RegisterAsync(duplicateRequest, CancellationToken.None));

        Assert.Equal("7654321", exception.ImoNumber);
    }

    [Fact]
    public async Task Register_rejects_unknown_vessel_type()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterVesselService(dbContext);
        var request = CreateValidRequest("1234567", Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<VesselTypeNotFoundException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(request.VesselTypeId, exception.VesselTypeId);
    }

    [Fact]
    public async Task Register_persists_vessel_and_returns_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var service = new RegisterVesselService(dbContext);
        var request = CreateValidRequest("1234567", vesselTypeId);
        var startedUtc = DateTime.UtcNow;

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var vessel = await dbContext
            .Set<Vessel>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, vessel.Id);
        Assert.Equal(request.CompanyId, vessel.CompanyId);
        Assert.Equal(vesselTypeId, vessel.VesselTypeId);
        Assert.Equal("North Star", vessel.Name);
        Assert.Equal("1234567", vessel.ImoNumber);
        Assert.Equal("Malta", vessel.FlagState);
        Assert.Equal("Active", vessel.CurrentStatus);
        Assert.InRange(vessel.CreatedAtUtc, startedUtc, DateTime.UtcNow);
        Assert.InRange(vessel.UpdatedAtUtc, startedUtc, DateTime.UtcNow);
    }

    private static RegisterVesselRequest CreateValidRequest(string imoNumber, Guid vesselTypeId)
    {
        return new RegisterVesselRequest
        {
            CompanyId = Guid.NewGuid(),
            VesselTypeId = vesselTypeId,
            Name = " North Star ",
            ImoNumber = $" {imoNumber} ",
            FlagState = " Malta ",
            CurrentStatus = " Active ",
        };
    }

    private static async Task<Guid> SeedVesselTypeAsync(HarborOpsDbContext dbContext)
    {
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
