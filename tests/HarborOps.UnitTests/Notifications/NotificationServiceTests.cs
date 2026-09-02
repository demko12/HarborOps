using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Notifications.Application;
using HarborOps.Modules.Notifications.Contracts;
using HarborOps.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Notifications;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task GenerateAsync_is_idempotent_for_the_same_source_expiry_and_threshold()
    {
        await using var dbContext = CreateDbContext();
        var service = new NotificationService(dbContext, TimeProvider.System);
        var sourceId = Guid.NewGuid();
        var expiry = new DateOnly(2026, 11, 29);

        await service.GenerateAsync("documents", "document", sourceId, Guid.NewGuid(), null, expiry, 90, CancellationToken.None);
        await service.GenerateAsync("documents", "document", sourceId, Guid.NewGuid(), null, expiry, 90, CancellationToken.None);

        var alert = Assert.Single(await dbContext.Set<OperationAlert>().ToListAsync());
        Assert.Equal("documents", alert.Capability);
        Assert.Equal("info", alert.Severity);
        Assert.Equal(sourceId, alert.SourceId);
    }

    [Fact]
    public async Task AcknowledgeAsync_is_owned_by_the_current_user_and_filters_unread_results()
    {
        await using var dbContext = CreateDbContext();
        var service = new NotificationService(dbContext, TimeProvider.System);
        var alert = new OperationAlert("crew", "certificate", Guid.NewGuid(), null, Guid.NewGuid(), new(2026, 9, 8), 7, "critical", DateTime.UtcNow);
        await dbContext.AddAsync(alert);
        await dbContext.SaveChangesAsync();
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();

        Assert.True(await service.AcknowledgeAsync(alert.Id, firstUser, new HashSet<string>(["crew"]), CancellationToken.None));

        var firstUserPage = await service.GetAsync(new("unread", 1, 25), firstUser, new HashSet<string>(["crew"]), CancellationToken.None);
        var secondUserPage = await service.GetAsync(new("unread", 1, 25), secondUser, new HashSet<string>(["crew"]), CancellationToken.None);

        Assert.Empty(firstUserPage.Items);
        Assert.Equal(0, firstUserPage.UnreadCount);
        Assert.Single(secondUserPage.Items);
        Assert.Equal(1, secondUserPage.UnreadCount);
    }

    [Fact]
    public async Task GetAsync_filters_by_capability_and_rejects_invalid_status()
    {
        await using var dbContext = CreateDbContext();
        var service = new NotificationService(dbContext, TimeProvider.System);
        await dbContext.AddRangeAsync(
            new OperationAlert("documents", "document", Guid.NewGuid(), Guid.NewGuid(), null, new(2026, 9, 8), 7, "critical", DateTime.UtcNow),
            new OperationAlert("compliance", "certificate", Guid.NewGuid(), Guid.NewGuid(), null, new(2026, 10, 1), 30, "warning", DateTime.UtcNow));
        await dbContext.SaveChangesAsync();

        var page = await service.GetAsync(new("all", 1, 25), Guid.NewGuid(), new HashSet<string>(["documents"]), CancellationToken.None);

        var alert = Assert.Single(page.Items);
        Assert.Equal("documents", alert.Capability);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new("invalid", 1, 25), Guid.NewGuid(), new HashSet<string>(["documents"]), CancellationToken.None));
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid():N}")
            .Options;
        return new HarborOpsDbContext(options);
    }
}
