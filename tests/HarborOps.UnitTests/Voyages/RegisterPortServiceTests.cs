using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RegisterPortServiceTests
{
    [Fact]
    public async Task Register_trims_values_and_normalizes_the_un_locode()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        var request = new RegisterPortRequest
        {
            Name = "  Port of Bar  ",
            Country = " Montenegro ",
            City = " Bar ",
            UnLocode = " mebar ",
            Latitude = 42.0936m,
            Longitude = 19.0894m,
            ShipyardName = "  Bar Shipyard  ",
        };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var port = await dbContext.Set<Port>().SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, port.Id);
        Assert.Equal("Port of Bar", port.Name);
        Assert.Equal("Montenegro", port.Country);
        Assert.Equal("Bar", port.City);
        Assert.Equal("MEBAR", port.UnLocode);
        Assert.Equal(42.0936m, port.Latitude);
        Assert.Equal(19.0894m, port.Longitude);
        Assert.Equal("Bar Shipyard", port.ShipyardName);
    }

    [Fact]
    public async Task Register_stores_a_port_without_optional_values()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        var request = new RegisterPortRequest
        {
            Name = "Port of Kotor",
            Country = "Montenegro",
            City = "   ",
        };

        await service.RegisterAsync(request, CancellationToken.None);

        var port = await dbContext.Set<Port>().SingleAsync(CancellationToken.None);

        Assert.Null(port.City);
        Assert.Null(port.UnLocode);
        Assert.Null(port.Latitude);
        Assert.Null(port.Longitude);
        Assert.Null(port.ShipyardName);
    }

    [Fact]
    public async Task Register_rejects_a_shipyard_name_that_is_too_long()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        var request = CreateValidRequest() with { ShipyardName = new string('A', Port.ShipyardNameMaxLength + 1) };

        var exception = await Assert.ThrowsAsync<RegisterPortValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPortValidator.ShipyardNameField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_requires_a_name_and_a_country()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        var request = new RegisterPortRequest { Name = " ", Country = null };

        var exception = await Assert.ThrowsAsync<RegisterPortValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPortValidator.NameField, exception.Errors.Keys, StringComparer.Ordinal);
        Assert.Contains(RegisterPortValidator.CountryField, exception.Errors.Keys, StringComparer.Ordinal);
        Assert.Empty(await dbContext.Set<Port>().ToListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("MEBA")]
    [InlineData("MEBAR1")]
    [InlineData("ME-BA")]
    public async Task Register_rejects_a_malformed_un_locode(string unLocode)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        var request = CreateValidRequest() with { UnLocode = unLocode };

        var exception = await Assert.ThrowsAsync<RegisterPortValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPortValidator.UnLocodeField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(91, 19)]
    [InlineData(-91, 19)]
    [InlineData(42, 181)]
    [InlineData(42, -181)]
    public async Task Register_rejects_coordinates_outside_their_range(
        decimal latitude,
        decimal longitude)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        var request = CreateValidRequest() with { Latitude = latitude, Longitude = longitude };

        var exception = await Assert.ThrowsAsync<RegisterPortValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.True(
            exception.Errors.ContainsKey(RegisterPortValidator.LatitudeField)
            || exception.Errors.ContainsKey(RegisterPortValidator.LongitudeField));
    }

    [Fact]
    public async Task Register_rejects_a_latitude_without_a_longitude()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        var request = CreateValidRequest() with { Latitude = 42.0936m, Longitude = null };

        var exception = await Assert.ThrowsAsync<RegisterPortValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterPortValidator.LongitudeField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_an_un_locode_that_differs_only_by_case()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);
        await service.RegisterAsync(
            CreateValidRequest() with { UnLocode = "MEBAR" },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateUnLocodeException>(
            () => service.RegisterAsync(
                CreateValidRequest() with { Name = "Bar terminal", UnLocode = "mebar" },
                CancellationToken.None));

        Assert.Equal("MEBAR", exception.UnLocode);
        Assert.Single(await dbContext.Set<Port>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Register_allows_several_ports_without_an_un_locode()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterPortService(dbContext);

        await service.RegisterAsync(
            CreateValidRequest() with { Name = "Port of Bar", UnLocode = null },
            CancellationToken.None);
        await service.RegisterAsync(
            CreateValidRequest() with { Name = "Port of Kotor", UnLocode = null },
            CancellationToken.None);

        Assert.Equal(2, await dbContext.Set<Port>().CountAsync(CancellationToken.None));
    }

    private static RegisterPortRequest CreateValidRequest()
    {
        return new RegisterPortRequest
        {
            Name = "Port of Bar",
            Country = "Montenegro",
            City = "Bar",
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
