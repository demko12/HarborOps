using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RegisterSupplierServiceTests
{
    [Fact]
    public async Task RegisterAsync_persists_a_trimmed_supplier()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterSupplierService(dbContext);

        var response = await service.RegisterAsync(
            new RegisterSupplierRequest
            {
                Name = "  Baltic Marine Parts  ",
                ContactPerson = "  Ana Ilic  ",
                PhoneEmail = " sales@baltic.test ",
                Address = " Pier 4 ",
                PaymentTerms = " Net 30 ",
            },
            CancellationToken.None);

        var stored = await dbContext.Set<Supplier>().SingleAsync(CancellationToken.None);

        Assert.Equal(stored.Id, response.SupplierId);
        Assert.Equal("Baltic Marine Parts", stored.Name);
        Assert.Equal("Ana Ilic", stored.ContactPerson);
        Assert.Equal("sales@baltic.test", stored.PhoneEmail);
        Assert.Equal("Pier 4", stored.Address);
        Assert.Equal("Net 30", stored.PaymentTerms);
    }

    [Fact]
    public async Task RegisterAsync_stores_blank_optional_fields_as_null()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterSupplierService(dbContext);

        await service.RegisterAsync(
            new RegisterSupplierRequest { Name = "Minimal supplier", ContactPerson = "   " },
            CancellationToken.None);

        var stored = await dbContext.Set<Supplier>().SingleAsync(CancellationToken.None);

        Assert.Null(stored.ContactPerson);
        Assert.Null(stored.PhoneEmail);
        Assert.Null(stored.Address);
        Assert.Null(stored.PaymentTerms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterAsync_requires_a_name(string? name)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterSupplierService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterSupplierValidationException>(
            () => service.RegisterAsync(
                new RegisterSupplierRequest { Name = name },
                CancellationToken.None));

        Assert.Contains(RegisterSupplierValidator.NameField, exception.Errors);
        Assert.Empty(await dbContext.Set<Supplier>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_rejects_an_overlong_name()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterSupplierService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterSupplierValidationException>(
            () => service.RegisterAsync(
                new RegisterSupplierRequest { Name = new string('x', Supplier.NameMaxLength + 1) },
                CancellationToken.None));

        Assert.Contains(RegisterSupplierValidator.NameField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_an_overlong_contact_person()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterSupplierService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterSupplierValidationException>(
            () => service.RegisterAsync(
                new RegisterSupplierRequest
                {
                    Name = "Baltic Marine Parts",
                    ContactPerson = new string('x', Supplier.ContactPersonMaxLength + 1),
                },
                CancellationToken.None));

        Assert.Contains(RegisterSupplierValidator.ContactPersonField, exception.Errors);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
