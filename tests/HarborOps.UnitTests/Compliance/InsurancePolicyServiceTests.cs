using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class InsurancePolicyServiceTests
{
    private static readonly DateTime AsOfUtc = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Register_rejects_expiry_dates_before_the_captured_current_date()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<InsurancePolicyValidationException>(
            () => service.RegisterAsync(CreateRequest() with { ExpiryDate = new DateOnly(2026, 8, 18) }, CancellationToken.None));

        Assert.Contains("expiryDate", exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_duplicate_policy_number_for_the_same_insurer()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = CreateRequest();
        await service.RegisterAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<DuplicateInsurancePolicyException>(
            () => service.RegisterAsync(request with { VesselId = Guid.NewGuid() }, CancellationToken.None));
    }

    [Fact]
    public async Task Get_filters_orders_and_classifies_policies_against_one_as_of_date()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        await dbContext.AddRangeAsync(
            new InsurancePolicy(vesselId, "Ocean Mutual", "EXPIRED", "P&I", new DateOnly(2026, 8, 18)),
            new InsurancePolicy(vesselId, "Ocean Mutual", "SOON", "Hull", new DateOnly(2026, 9, 18)),
            new InsurancePolicy(vesselId, "Ocean Mutual", "ACTIVE", "Hull", new DateOnly(2026, 9, 19)));
        await dbContext.SaveChangesAsync();

        var response = await CreateService(dbContext).GetAsync(new()
        {
            VesselId = vesselId,
            PageNumber = 1,
            PageSize = 25,
        }, CancellationToken.None);

        Assert.Equal(["EXPIRED", "SOON", "ACTIVE"], response.Items.Select(item => item.PolicyNumber));
        Assert.Equal(["Expired", "Expiring soon", "Active"], response.Items.Select(item => item.ExpiryClassification));
    }

    private static InsurancePolicyService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FakeDateTimeProvider(AsOfUtc));

    private static RegisterInsurancePolicyRequest CreateRequest() =>
        new(Guid.NewGuid(), " Harbor Mutual ", " HM-2026-001 ", " P&I ", new DateOnly(2027, 1, 1));

    private static HarborOpsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<HarborOpsDbContext>()
        .UseInMemoryDatabase($"HarborOpsInsurancePolicyTests-{Guid.NewGuid()}")
        .Options);
}
