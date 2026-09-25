using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class DocumentRequirementServiceTests
{
    private static readonly Guid VesselId = Guid.NewGuid();

    [Fact]
    public async Task CreateAsync_persists_the_requirement_and_its_created_history()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        var actorId = Guid.NewGuid();

        var result = await CreateService(dbContext).CreateAsync(
            actorId,
            new CreateDocumentRequirementRequest
            {
                VesselId = VesselId,
                DocumentTypeId = documentType.Id,
                MinimumRemainingValidityDays = 30,
                Instruction = "  Keep the original on board.  ",
                Reason = "  Flag state circular 12/2026.  ",
            },
            CancellationToken.None);

        Assert.Equal(documentType.Name, result.DocumentTypeName);
        Assert.True(result.IsEnabled);
        Assert.Equal("Keep the original on board.", result.Instruction);
        Assert.Equal(1, result.Revision);
        Assert.Equal(DocumentRequirementTestData.FixedUtcNow.UtcDateTime, result.UpdatedAtUtc);

        var history = await dbContext.Set<VesselDocumentRequirementHistory>().SingleAsync();
        Assert.Equal(result.Id, history.RequirementId);
        Assert.Equal(VesselDocumentRequirementHistory.CreatedEvent, history.EventType);
        Assert.Equal(actorId, history.ActorUserId);
        Assert.Equal("Flag state circular 12/2026.", history.Reason);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_vessel_unknown_to_fleet()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);

        await Assert.ThrowsAsync<DocumentRequirementVesselNotFoundException>(() => CreateService(dbContext).CreateAsync(
            Guid.NewGuid(),
            ValidCreateRequest(Guid.NewGuid(), documentType.Id),
            CancellationToken.None));

        Assert.Empty(dbContext.Set<VesselDocumentRequirement>());
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_document_type()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        await Assert.ThrowsAsync<DocumentTypeNotFoundException>(() => CreateService(dbContext).CreateAsync(
            Guid.NewGuid(),
            ValidCreateRequest(VesselId, 999),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_second_requirement_for_the_same_vessel_and_type()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id, isEnabled: false);

        await Assert.ThrowsAsync<DuplicateDocumentRequirementException>(() => CreateService(dbContext).CreateAsync(
            Guid.NewGuid(),
            ValidCreateRequest(VesselId, documentType.Id),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_returns_field_errors_for_invalid_input()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        var exception = await Assert.ThrowsAsync<DocumentRequirementValidationException>(() => CreateService(dbContext).CreateAsync(
            Guid.NewGuid(),
            new CreateDocumentRequirementRequest
            {
                MinimumRemainingValidityDays = 366,
                Instruction = new string('x', VesselDocumentRequirement.InstructionMaxLength + 1),
            },
            CancellationToken.None));

        Assert.Contains(DocumentRequirementValidator.VesselIdField, exception.Errors.Keys);
        Assert.Contains(DocumentRequirementValidator.DocumentTypeIdField, exception.Errors.Keys);
        Assert.Contains(DocumentRequirementValidator.MinimumRemainingValidityDaysField, exception.Errors.Keys);
        Assert.Contains(DocumentRequirementValidator.InstructionField, exception.Errors.Keys);
        Assert.Contains(DocumentRequirementValidator.ReasonField, exception.Errors.Keys);
    }

    [Fact]
    public async Task UpdateAsync_applies_the_change_and_records_a_reasoned_revision()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        var requirement = await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var actorId = Guid.NewGuid();
        var previousVersion = requirement.ConcurrencyToken;

        var result = await CreateService(dbContext).UpdateAsync(
            requirement.Id,
            actorId,
            new UpdateDocumentRequirementRequest
            {
                IsEnabled = true,
                MinimumRemainingValidityDays = 90,
                Instruction = null,
                Reason = "Charter requires 90 days.",
                Version = requirement.ConcurrencyToken,
            },
            CancellationToken.None);

        Assert.Equal(90, result.MinimumRemainingValidityDays);
        Assert.Equal(2, result.Revision);
        Assert.NotEqual(previousVersion, result.Version);

        var history = await dbContext.Set<VesselDocumentRequirementHistory>()
            .Where(entry => entry.RequirementId == requirement.Id)
            .OrderBy(entry => entry.Revision)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(VesselDocumentRequirementHistory.UpdatedEvent, history[1].EventType);
        Assert.Equal("Charter requires 90 days.", history[1].Reason);
        Assert.Equal(actorId, history[1].ActorUserId);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_stale_version_without_changing_anything()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        var requirement = await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);

        await Assert.ThrowsAsync<DocumentRequirementConflictException>(() => CreateService(dbContext).UpdateAsync(
            requirement.Id,
            Guid.NewGuid(),
            new UpdateDocumentRequirementRequest
            {
                IsEnabled = true,
                MinimumRemainingValidityDays = 90,
                Reason = "Stale edit.",
                Version = Guid.NewGuid(),
            },
            CancellationToken.None));

        Assert.Equal(1, await dbContext.Set<VesselDocumentRequirementHistory>().CountAsync());
        Assert.Equal(0, (await dbContext.Set<VesselDocumentRequirement>().SingleAsync()).MinimumRemainingValidityDays);
    }

    [Fact]
    public async Task UpdateAsync_requires_reason_and_version()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        var exception = await Assert.ThrowsAsync<DocumentRequirementValidationException>(() => CreateService(dbContext).UpdateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateDocumentRequirementRequest { IsEnabled = true, MinimumRemainingValidityDays = 0 },
            CancellationToken.None));

        Assert.Contains(DocumentRequirementValidator.ReasonField, exception.Errors.Keys);
        Assert.Contains(DocumentRequirementValidator.VersionField, exception.Errors.Keys);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unknown_requirement()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        await Assert.ThrowsAsync<DocumentRequirementNotFoundException>(() => CreateService(dbContext).UpdateAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateDocumentRequirementRequest
            {
                IsEnabled = true,
                MinimumRemainingValidityDays = 0,
                Reason = "Edit.",
                Version = Guid.NewGuid(),
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task DisableAsync_disables_and_records_the_reason()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        var requirement = await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);

        var result = await CreateService(dbContext).DisableAsync(
            requirement.Id,
            Guid.NewGuid(),
            new DisableDocumentRequirementRequest { Reason = "Vessel sold.", Version = requirement.ConcurrencyToken },
            CancellationToken.None);

        Assert.False(result.IsEnabled);
        var history = await dbContext.Set<VesselDocumentRequirementHistory>()
            .SingleAsync(entry => entry.EventType == VesselDocumentRequirementHistory.DisabledEvent);
        Assert.Equal("Vessel sold.", history.Reason);
    }

    [Fact]
    public async Task DisableAsync_rejects_an_already_disabled_requirement()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        var requirement = await DocumentRequirementTestData.SeedRequirementAsync(
            dbContext, VesselId, documentType.Id, isEnabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(dbContext).DisableAsync(
            requirement.Id,
            Guid.NewGuid(),
            new DisableDocumentRequirementRequest { Reason = "Again.", Version = requirement.ConcurrencyToken },
            CancellationToken.None));
    }

    private static CreateDocumentRequirementRequest ValidCreateRequest(Guid vesselId, int documentTypeId) => new()
    {
        VesselId = vesselId,
        DocumentTypeId = documentTypeId,
        MinimumRemainingValidityDays = 0,
        Reason = "Company requirement.",
    };

    private static DocumentRequirementService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FakeDocumentsVesselLookup(VesselId), new FixedTimeProvider(DocumentRequirementTestData.FixedUtcNow));
}
