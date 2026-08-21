using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetSuppliersQueryTests
{
    [Fact]
    public async Task GetSuppliersAsync_projects_the_directory_row()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts", "Ana Ilic");

        var response = await new GetSuppliersQuery(dbContext).GetSuppliersAsync(
            new GetSuppliersRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(supplier.Id, item.SupplierId);
        Assert.Equal("Baltic Marine Parts", item.Name);
        Assert.Equal("Ana Ilic", item.ContactPerson);
        Assert.Equal(1, response.PageNumber);
        Assert.Equal(GetSuppliersValidator.DefaultPageSize, response.PageSize);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task GetSuppliersAsync_searches_name_and_contact_person()
    {
        await using var dbContext = CreateDbContext();
        var byName = await SeedSupplierAsync(dbContext, "Nordic Filters", "Petar Petrovic");
        var byContact = await SeedSupplierAsync(dbContext, "Adriatic Supplies", "Nordic Agent");
        await SeedSupplierAsync(dbContext, "Aegean Trading", "Marko Markovic");

        var response = await new GetSuppliersQuery(dbContext).GetSuppliersAsync(
            new GetSuppliersRequest { Search = "Nordic" },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Contains(response.Items, item => item.SupplierId == byName.Id);
        Assert.Contains(response.Items, item => item.SupplierId == byContact.Id);
    }

    [Fact]
    public async Task GetSuppliersAsync_orders_by_name()
    {
        await using var dbContext = CreateDbContext();
        await SeedSupplierAsync(dbContext, "Zadar Marine", null);
        await SeedSupplierAsync(dbContext, "Adriatic Supplies", null);
        await SeedSupplierAsync(dbContext, "Nordic Filters", null);

        var response = await new GetSuppliersQuery(dbContext).GetSuppliersAsync(
            new GetSuppliersRequest(),
            CancellationToken.None);

        Assert.Equal(
            ["Adriatic Supplies", "Nordic Filters", "Zadar Marine"],
            response.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task GetSuppliersAsync_pages_deterministically()
    {
        await using var dbContext = CreateDbContext();
        await SeedSupplierAsync(dbContext, "Supplier A", null);
        await SeedSupplierAsync(dbContext, "Supplier B", null);
        await SeedSupplierAsync(dbContext, "Supplier C", null);

        var query = new GetSuppliersQuery(dbContext);

        var firstPage = await query.GetSuppliersAsync(
            new GetSuppliersRequest { PageNumber = 1, PageSize = 2 },
            CancellationToken.None);
        var secondPage = await query.GetSuppliersAsync(
            new GetSuppliersRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(["Supplier A", "Supplier B"], firstPage.Items.Select(item => item.Name));
        Assert.Equal(["Supplier C"], secondPage.Items.Select(item => item.Name));
    }

    [Theory]
    [InlineData(0, null, GetSuppliersValidator.PageNumberField)]
    [InlineData(null, 0, GetSuppliersValidator.PageSizeField)]
    [InlineData(null, GetSuppliersValidator.MaxPageSize + 1, GetSuppliersValidator.PageSizeField)]
    public async Task GetSuppliersAsync_rejects_invalid_paging(
        int? pageNumber,
        int? pageSize,
        string expectedField)
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetSuppliersValidationException>(
            () => new GetSuppliersQuery(dbContext).GetSuppliersAsync(
                new GetSuppliersRequest { PageNumber = pageNumber, PageSize = pageSize },
                CancellationToken.None));

        Assert.Contains(expectedField, exception.Errors);
    }

    private static async Task<Supplier> SeedSupplierAsync(
        HarborOpsDbContext dbContext,
        string name,
        string? contactPerson)
    {
        var supplier = Supplier.Register(name, contactPerson, null, null, null);

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync();

        return supplier;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
