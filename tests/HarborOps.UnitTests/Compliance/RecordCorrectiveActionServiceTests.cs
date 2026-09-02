using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class RecordCorrectiveActionServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_first_action_on_open_finding_transitions_to_in_progress_and_persists()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        int nonConformityId;

        await using (var dbContext = CreateDbContext(databaseName))
        {
            var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);
            nonConformityId = nonConformity.Id;

            var response = await CreateService(dbContext).RecordAsync(
                nonConformityId,
                Guid.NewGuid(),
                new RecordCorrectiveActionRequest("Inspected the valve.", null),
                CancellationToken.None);

            Assert.Equal(NonConformity.ClosureStatusInProgress, response.ClosureStatus);
        }

        await using var verificationContext = CreateDbContext(databaseName);
        var persistedNonConformity = await verificationContext.Set<NonConformity>()
            .AsNoTracking()
            .SingleAsync(n => n.Id == nonConformityId);
        var persistedAction = await verificationContext.Set<CorrectiveAction>()
            .AsNoTracking()
            .SingleAsync(a => a.NonConformityId == nonConformityId);

        Assert.Equal(NonConformity.ClosureStatusInProgress, persistedNonConformity.ClosureStatus);
        Assert.Equal("Inspected the valve.", persistedAction.ActionText);
    }

    [Fact]
    public async Task RecordAsync_second_action_on_in_progress_finding_keeps_status_and_adds_a_second_action()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);
        var service = CreateService(dbContext);

        await service.RecordAsync(
            nonConformity.Id, Guid.NewGuid(), new RecordCorrectiveActionRequest("First action.", null), CancellationToken.None);
        var secondResponse = await service.RecordAsync(
            nonConformity.Id, Guid.NewGuid(), new RecordCorrectiveActionRequest("Second action.", null), CancellationToken.None);

        Assert.Equal(NonConformity.ClosureStatusInProgress, secondResponse.ClosureStatus);
        var actions = await dbContext.Set<CorrectiveAction>()
            .Where(a => a.NonConformityId == nonConformity.Id)
            .ToListAsync();
        Assert.Equal(2, actions.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordAsync_rejects_missing_action_text(string? actionText)
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordCorrectiveActionValidationException>(
            () => service.RecordAsync(
                nonConformity.Id, Guid.NewGuid(), new RecordCorrectiveActionRequest(actionText, null), CancellationToken.None));

        Assert.Contains(RecordCorrectiveActionValidator.ActionTextField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_action_text_exceeding_the_max_length()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);
        var service = CreateService(dbContext);
        var tooLong = new string('a', CorrectiveAction.ActionTextMaxLength + 1);

        var exception = await Assert.ThrowsAsync<RecordCorrectiveActionValidationException>(
            () => service.RecordAsync(
                nonConformity.Id, Guid.NewGuid(), new RecordCorrectiveActionRequest(tooLong, null), CancellationToken.None));

        Assert.Contains(RecordCorrectiveActionValidator.ActionTextField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_throws_for_an_unknown_non_conformity()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        const int unknownId = 999_999;

        var exception = await Assert.ThrowsAsync<NonConformityNotFoundException>(
            () => service.RecordAsync(
                unknownId, Guid.NewGuid(), new RecordCorrectiveActionRequest("Action.", null), CancellationToken.None));

        Assert.Equal(unknownId, exception.NonConformityId);
    }

    [Fact]
    public async Task RecordAsync_throws_for_an_already_closed_finding()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusClosed);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidNonConformityTransitionException>(
            () => service.RecordAsync(
                nonConformity.Id, Guid.NewGuid(), new RecordCorrectiveActionRequest("Too late.", null), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_derives_actor_and_time_from_the_parameter_and_the_clock()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);
        var service = CreateService(dbContext);
        var actorUserId = Guid.NewGuid();
        var evidenceDocumentId = Guid.NewGuid();

        await service.RecordAsync(
            nonConformity.Id,
            actorUserId,
            new RecordCorrectiveActionRequest("Action taken.", evidenceDocumentId),
            CancellationToken.None);

        var persistedAction = await dbContext.Set<CorrectiveAction>()
            .SingleAsync(a => a.NonConformityId == nonConformity.Id);

        Assert.Equal(actorUserId, persistedAction.ActorUserId);
        Assert.Equal(FixedNow, persistedAction.RecordedAtUtc);
        Assert.Equal(evidenceDocumentId, persistedAction.EvidenceDocumentId);
    }

    [Fact]
    public async Task RecordAsync_trims_action_text_before_storing()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);
        var service = CreateService(dbContext);

        await service.RecordAsync(
            nonConformity.Id, Guid.NewGuid(), new RecordCorrectiveActionRequest("  Padded action text.  ", null), CancellationToken.None);

        var persistedAction = await dbContext.Set<CorrectiveAction>()
            .SingleAsync(a => a.NonConformityId == nonConformity.Id);
        Assert.Equal("Padded action text.", persistedAction.ActionText);
    }

    private static RecordCorrectiveActionService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static async Task<NonConformity> SeedNonConformityAsync(HarborOpsDbContext dbContext, string closureStatus)
    {
        var nonConformity = new NonConformity(
            Guid.NewGuid(),
            NonConformity.SeverityMajor,
            new DateOnly(2026, 12, 1),
            closureStatus);

        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        return nonConformity;
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
