using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class RegisterPscInspectionServiceTests
{
    [Fact]
    public async Task Register_creates_inspection_and_trims_text()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPscInspectionService(dbContext);
        var request = CreateValidRequest();

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var inspection = await dbContext
            .Set<PscInspection>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(response.Id, inspection.Id);
        Assert.Equal(request.VesselId, inspection.VesselId);
        Assert.Equal(request.PortId, inspection.PortId);
        Assert.Equal(request.InspectionDate, inspection.Date);
        Assert.Equal("Port Authority", inspection.InspectingAuthority);
        Assert.Equal(request.DeficiencyCount, inspection.DeficiencyCount);
        Assert.Equal(request.VesselDetained, inspection.VesselDetained);
    }

    [Fact]
    public async Task Register_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPscInspectionService(dbContext);
        var request = CreateValidRequest() with { VesselId = Guid.Empty };

        var exception = await Assert.ThrowsAsync<RegisterPscInspectionValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPscInspectionValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_empty_port_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPscInspectionService(dbContext);
        var request = CreateValidRequest() with { PortId = Guid.Empty };

        var exception = await Assert.ThrowsAsync<RegisterPscInspectionValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPscInspectionValidator.PortIdField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_future_inspection_date()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPscInspectionService(dbContext);
        var request = CreateValidRequest() with
        {
            InspectionDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
        };

        var exception = await Assert.ThrowsAsync<RegisterPscInspectionValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPscInspectionValidator.InspectionDateField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_negative_deficiency_count()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPscInspectionService(dbContext);
        var request = CreateValidRequest() with { DeficiencyCount = -1 };

        var exception = await Assert.ThrowsAsync<RegisterPscInspectionValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPscInspectionValidator.DeficiencyCountField, exception.Errors);
    }

    private static RegisterPscInspectionRequest CreateValidRequest()
    {
        return new RegisterPscInspectionRequest
        {
            VesselId = Guid.NewGuid(),
            PortId = Guid.NewGuid(),
            InspectionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InspectingAuthority = " Port Authority ",
            DeficiencyCount = 2,
            VesselDetained = false,
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
