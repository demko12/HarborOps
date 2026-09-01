using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class CloseNonConformityServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CloseAsync_closes_an_in_progress_finding_and_persists_all_closure_fields()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        int nonConformityId;
        var closedByUserId = Guid.NewGuid();
        var finalEvidenceDocumentId = Guid.NewGuid();

        await using (var dbContext = CreateDbContext(databaseName))
        {
            var nonConformity = await SeedNonConformityWithPriorActionAsync(dbContext);
            nonConformityId = nonConformity.Id;

            var response = await CreateService(dbContext).CloseAsync(
                nonConformityId,
                closedByUserId,
                new CloseNonConformityRequest("All corrective actions verified.", finalEvidenceDocumentId),
                CancellationToken.None);

            Assert.Equal(NonConformity.ClosureStatusClosed, response.ClosureStatus);
            Assert.Equal(FixedNow, response.ClosedAtUtc);
        }

        await using var verificationContext = CreateDbContext(databaseName);
        var persisted = await verificationContext.Set<NonConformity>()
            .AsNoTracking()
            .SingleAsync(n => n.Id == nonConformityId);

        Assert.Equal(NonConformity.ClosureStatusClosed, persisted.ClosureStatus);
        Assert.Equal("All corrective actions verified.", persisted.ClosureNotes);
        Assert.Equal(closedByUserId, persisted.ClosedByUserId);
        Assert.Equal(FixedNow, persisted.ClosedAtUtc);
        Assert.Equal(finalEvidenceDocumentId, persisted.FinalEvidenceDocumentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CloseAsync_rejects_missing_closure_notes(string? closureNotes)
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityWithPriorActionAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CloseNonConformityValidationException>(
            () => service.CloseAsync(
                nonConformity.Id, Guid.NewGuid(), new CloseNonConformityRequest(closureNotes, null), CancellationToken.None));

        Assert.Contains(CloseNonConformityValidator.ClosureNotesField, exception.Errors);
    }

    [Fact]
    public async Task CloseAsync_rejects_closure_notes_exceeding_the_max_length()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityWithPriorActionAsync(dbContext);
        var service = CreateService(dbContext);
        var tooLong = new string('a', NonConformity.ClosureNotesMaxLength + 1);

        var exception = await Assert.ThrowsAsync<CloseNonConformityValidationException>(
            () => service.CloseAsync(
                nonConformity.Id, Guid.NewGuid(), new CloseNonConformityRequest(tooLong, null), CancellationToken.None));

        Assert.Contains(CloseNonConformityValidator.ClosureNotesField, exception.Errors);
    }

    [Fact]
    public async Task CloseAsync_throws_for_an_unknown_non_conformity()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        const int unknownId = 999_999;

        var exception = await Assert.ThrowsAsync<NonConformityNotFoundException>(
            () => service.CloseAsync(
                unknownId, Guid.NewGuid(), new CloseNonConformityRequest("Notes.", null), CancellationToken.None));

        Assert.Equal(unknownId, exception.NonConformityId);
    }

    [Fact]
    public async Task CloseAsync_throws_when_closing_an_open_finding_with_no_actions()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidNonConformityTransitionException>(
            () => service.CloseAsync(
                nonConformity.Id, Guid.NewGuid(), new CloseNonConformityRequest("Notes.", null), CancellationToken.None));
    }

    [Fact]
    public async Task CloseAsync_throws_for_an_already_closed_finding()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusClosed);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidNonConformityTransitionException>(
            () => service.CloseAsync(
                nonConformity.Id, Guid.NewGuid(), new CloseNonConformityRequest("Notes.", null), CancellationToken.None));
    }

    [Fact]
    public async Task CloseAsync_derives_closed_by_and_time_from_the_parameter_and_the_clock()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityWithPriorActionAsync(dbContext);
        var service = CreateService(dbContext);
        var closedByUserId = Guid.NewGuid();

        await service.CloseAsync(
            nonConformity.Id, closedByUserId, new CloseNonConformityRequest("Notes.", null), CancellationToken.None);

        Assert.Equal(closedByUserId, nonConformity.ClosedByUserId);
        Assert.Equal(FixedNow, nonConformity.ClosedAtUtc);
    }

    [Fact]
    public async Task CloseAsync_trims_closure_notes_before_storing()
    {
        await using var dbContext = CreateDbContext();
        var nonConformity = await SeedNonConformityWithPriorActionAsync(dbContext);
        var service = CreateService(dbContext);

        await service.CloseAsync(
            nonConformity.Id, Guid.NewGuid(), new CloseNonConformityRequest("  Padded notes.  ", null), CancellationToken.None);

        Assert.Equal("Padded notes.", nonConformity.ClosureNotes);
    }

    private static CloseNonConformityService CreateService(HarborOpsDbContext dbContext) =>
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

    private static async Task<NonConformity> SeedNonConformityWithPriorActionAsync(HarborOpsDbContext dbContext)
    {
        var nonConformity = await SeedNonConformityAsync(dbContext, NonConformity.ClosureStatusOpen);

        nonConformity.AddCorrectiveAction("Initial corrective action.", Guid.NewGuid(), FixedNow.AddDays(-1), null);
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
