using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RegisterBerthServiceTests
{
    [Fact]
    public async Task Register_persists_the_berth_under_its_port()
    {
        await using var dbContext = CreateDbContext();
        var portId = await SeedPortAsync(dbContext, "Port of Bar");
        var service = new RegisterBerthService(dbContext);
        var request = new RegisterBerthRequest
        {
            PortId = portId,
            Name = "  Berth 1  ",
            Length = 210.5m,
            MaxDraft = 12.4m,
            Status = " Available ",
        };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var berth = await dbContext.Set<Berth>().SingleAsync(CancellationToken.None);

        Assert.Equal(response.Id, berth.Id);
        Assert.Equal(portId, berth.PortId);
        Assert.Equal("Berth 1", berth.Name);
        Assert.Equal(210.5m, berth.Length);
        Assert.Equal(12.4m, berth.MaxDraft);
        Assert.Equal("Available", berth.Status);
    }

    [Fact]
    public async Task Register_rejects_an_unknown_port()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterBerthService(dbContext);
        var missingPortId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PortNotFoundException>(
            () => service.RegisterAsync(
                CreateValidRequest(missingPortId),
                CancellationToken.None));

        Assert.Equal(missingPortId, exception.PortId);
        Assert.Empty(await dbContext.Set<Berth>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Register_rejects_a_berth_name_that_differs_only_by_case_within_the_port()
    {
        await using var dbContext = CreateDbContext();
        var portId = await SeedPortAsync(dbContext, "Port of Bar");
        var service = new RegisterBerthService(dbContext);
        await service.RegisterAsync(CreateValidRequest(portId), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateBerthNameException>(
            () => service.RegisterAsync(
                CreateValidRequest(portId) with { Name = "berth 1" },
                CancellationToken.None));

        Assert.Equal(portId, exception.PortId);
        Assert.Single(await dbContext.Set<Berth>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Register_allows_the_same_berth_name_in_another_port()
    {
        await using var dbContext = CreateDbContext();
        var firstPortId = await SeedPortAsync(dbContext, "Port of Bar");
        var secondPortId = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = new RegisterBerthService(dbContext);

        await service.RegisterAsync(CreateValidRequest(firstPortId), CancellationToken.None);
        await service.RegisterAsync(CreateValidRequest(secondPortId), CancellationToken.None);

        Assert.Equal(2, await dbContext.Set<Berth>().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Register_requires_a_port_a_name_and_a_status()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterBerthService(dbContext);
        var request = new RegisterBerthRequest
        {
            PortId = Guid.Empty,
            Name = " ",
            Length = 210m,
            MaxDraft = 12m,
            Status = null,
        };

        var exception = await Assert.ThrowsAsync<RegisterBerthValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterBerthValidator.PortIdField, exception.Errors.Keys, StringComparer.Ordinal);
        Assert.Contains(RegisterBerthValidator.NameField, exception.Errors.Keys, StringComparer.Ordinal);
        Assert.Contains(RegisterBerthValidator.StatusField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Available")]
    [InlineData("Occupied")]
    [InlineData("Reserved")]
    [InlineData("Out of service")]
    public async Task Register_accepts_every_berth_status_in_circulation(string status)
    {
        await using var dbContext = CreateDbContext();
        var portId = await SeedPortAsync(dbContext, "Port of Bar");
        var service = new RegisterBerthService(dbContext);

        await service.RegisterAsync(
            CreateValidRequest(portId) with { Status = status },
            CancellationToken.None);

        var berth = await dbContext.Set<Berth>().SingleAsync(CancellationToken.None);

        Assert.Equal(status, berth.Status);
    }

    [Theory]
    [InlineData("Operational")]
    [InlineData("available")]
    [InlineData("Slobodan")]
    public async Task Register_rejects_a_status_outside_the_allowed_list(string status)
    {
        await using var dbContext = CreateDbContext();
        var portId = await SeedPortAsync(dbContext, "Port of Bar");
        var service = new RegisterBerthService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterBerthValidationException>(
            () => service.RegisterAsync(
                CreateValidRequest(portId) with { Status = status },
                CancellationToken.None));

        Assert.Contains(
            "Status must be one of: Available, Occupied, Reserved, Out of service.",
            exception.Errors[RegisterBerthValidator.StatusField],
            StringComparer.Ordinal);
        Assert.Empty(await dbContext.Set<Berth>().ToListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(-1, 12)]
    [InlineData(210, 0)]
    [InlineData(210, -1)]
    public async Task Register_requires_positive_dimensions(decimal length, decimal maxDraft)
    {
        await using var dbContext = CreateDbContext();
        var portId = await SeedPortAsync(dbContext, "Port of Bar");
        var service = new RegisterBerthService(dbContext);
        var request = CreateValidRequest(portId) with { Length = length, MaxDraft = maxDraft };

        var exception = await Assert.ThrowsAsync<RegisterBerthValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.True(
            exception.Errors.ContainsKey(RegisterBerthValidator.LengthField)
            || exception.Errors.ContainsKey(RegisterBerthValidator.MaxDraftField));
    }

    private static RegisterBerthRequest CreateValidRequest(Guid portId)
    {
        return new RegisterBerthRequest
        {
            PortId = portId,
            Name = "Berth 1",
            Length = 210m,
            MaxDraft = 12m,
            Status = "Available",
        };
    }

    private static async Task<Guid> SeedPortAsync(HarborOpsDbContext dbContext, string name)
    {
        var port = Port.Create(name, "Montenegro");

        await dbContext.Set<Port>().AddAsync(port, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return port.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
