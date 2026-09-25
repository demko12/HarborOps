using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class DocumentRequirementRelationalTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 9, 24);

    private static readonly Guid VesselId = Guid.Parse("7c1f0b3e-2d4a-4f5b-9e6c-0a1b2c3d4e5f");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task The_migration_creates_the_requirement_tables_in_the_documents_schema()
    {
        await using var dbContext = CreateDbContext();

        var tables = await QueryStringsAsync(
            dbContext,
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'documents'");

        Assert.Contains("VesselDocumentRequirements", tables);
        Assert.Contains("VesselDocumentRequirementHistories", tables);
    }

    [RelationalFact]
    public async Task The_database_allows_only_one_requirement_per_vessel_and_type()
    {
        var documentTypeId = await SeedDocumentTypeAsync();

        await using (var firstContext = CreateDbContext())
        {
            await firstContext.Set<VesselDocumentRequirement>().AddAsync(
                VesselDocumentRequirement.Create(VesselId, documentTypeId, 0, null, Guid.NewGuid(), FixedUtcNow.UtcDateTime));
            await firstContext.SaveChangesAsync();
        }

        await using var secondContext = CreateDbContext();
        await secondContext.Set<VesselDocumentRequirement>().AddAsync(
            VesselDocumentRequirement.Create(VesselId, documentTypeId, 30, null, Guid.NewGuid(), FixedUtcNow.UtcDateTime));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    [RelationalFact]
    public async Task Simultaneous_creates_for_the_same_vessel_and_type_store_exactly_one_requirement()
    {
        var documentTypeId = await SeedDocumentTypeAsync();
        var request = new CreateDocumentRequirementRequest
        {
            VesselId = VesselId,
            DocumentTypeId = documentTypeId,
            MinimumRemainingValidityDays = 0,
            Reason = "Company requirement.",
        };

        await using var firstContext = CreateDbContext();
        await using var secondContext = CreateDbContext();

        var attempts = await Task.WhenAll(
            TryCreateAsync(firstContext, request),
            TryCreateAsync(secondContext, request));

        Assert.Single(attempts, succeeded => succeeded);

        await using var verifyContext = CreateDbContext();
        Assert.Equal(1, await verifyContext.Set<VesselDocumentRequirement>().CountAsync());
        Assert.Equal(1, await verifyContext.Set<VesselDocumentRequirementHistory>().CountAsync());
    }

    [RelationalFact]
    public async Task A_concurrent_requirement_edit_is_rejected_and_leaves_one_revision()
    {
        var documentTypeId = await SeedDocumentTypeAsync();
        var requirementId = await SeedRequirementAsync(documentTypeId, minimumRemainingValidityDays: 0);

        await using var firstContext = CreateDbContext();
        await using var secondContext = CreateDbContext();

        var first = await firstContext.Set<VesselDocumentRequirement>().SingleAsync(requirement => requirement.Id == requirementId);
        var second = await secondContext.Set<VesselDocumentRequirement>().SingleAsync(requirement => requirement.Id == requirementId);

        first.Update(true, 30, null, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        await firstContext.Set<VesselDocumentRequirementHistory>().AddAsync(
            VesselDocumentRequirementHistory.RecordUpdated(first, "Charter requires 30 days."));
        await firstContext.SaveChangesAsync();

        second.Update(true, 90, null, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        await secondContext.Set<VesselDocumentRequirementHistory>().AddAsync(
            VesselDocumentRequirementHistory.RecordUpdated(second, "Charter requires 90 days."));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());

        await using var verifyContext = CreateDbContext();
        var stored = await verifyContext.Set<VesselDocumentRequirement>().SingleAsync(requirement => requirement.Id == requirementId);
        Assert.Equal(30, stored.MinimumRemainingValidityDays);
        Assert.Equal(2, stored.Revision);
        Assert.Equal(2, await verifyContext.Set<VesselDocumentRequirementHistory>().CountAsync());
    }

    [RelationalFact]
    public async Task A_stale_version_is_rejected_by_the_service()
    {
        var documentTypeId = await SeedDocumentTypeAsync();
        var requirementId = await SeedRequirementAsync(documentTypeId, minimumRemainingValidityDays: 0);
        Guid staleVersion;

        await using (var readContext = CreateDbContext())
        {
            staleVersion = await readContext.Set<VesselDocumentRequirement>()
                .Where(requirement => requirement.Id == requirementId)
                .Select(requirement => requirement.ConcurrencyToken)
                .SingleAsync();
        }

        await using (var firstContext = CreateDbContext())
        {
            await CreateService(firstContext).UpdateAsync(
                requirementId,
                Guid.NewGuid(),
                UpdateRequest(30, staleVersion),
                CancellationToken.None);
        }

        await using var secondContext = CreateDbContext();

        await Assert.ThrowsAsync<DocumentRequirementConflictException>(() => CreateService(secondContext).UpdateAsync(
            requirementId,
            Guid.NewGuid(),
            UpdateRequest(90, staleVersion),
            CancellationToken.None));
    }

    [RelationalFact]
    public async Task Readiness_is_evaluated_on_sql_server_with_the_validity_buffer_boundary()
    {
        var documentTypeId = await SeedDocumentTypeAsync();
        var otherTypeId = await SeedDocumentTypeAsync("International Oil Pollution Prevention Certificate");
        await SeedRequirementAsync(documentTypeId, minimumRemainingValidityDays: 30);
        await SeedRequirementAsync(otherTypeId, minimumRemainingValidityDays: 30);
        var requiredThrough = Today.AddDays(60);
        var qualifying = await SeedApprovedDocumentAsync(documentTypeId, requiredThrough.AddDays(30));
        await SeedApprovedDocumentAsync(otherTypeId, requiredThrough.AddDays(29));

        await using var dbContext = CreateDbContext();
        var result = await CreateReadinessQuery(dbContext).GetAsync(
            new GetDocumentReadinessRequest { VesselId = VesselId, RequiredThrough = requiredThrough },
            restrictToReadOnlyVisible: false,
            CancellationToken.None);

        Assert.Equal(new DocumentReadinessSummary(2, 1, 1), result.Summary);
        var satisfied = Assert.Single(result.Items, item => item.Status == DocumentReadinessRules.SatisfiedStatus);
        Assert.Equal(qualifying, satisfied.SelectedDocumentId);
        var unsatisfied = Assert.Single(result.Items, item => item.Status == DocumentReadinessRules.UnsatisfiedStatus);
        Assert.Equal([DocumentReadinessRules.InsufficientValidity], unsatisfied.Blockers);
    }

    [RelationalFact]
    public async Task Readiness_on_sql_server_excludes_archived_documents_and_counts_current_acknowledgements()
    {
        var documentTypeId = await SeedDocumentTypeAsync();
        await SeedRequirementAsync(documentTypeId, minimumRemainingValidityDays: 0);
        var documentId = await SeedApprovedDocumentAsync(documentTypeId, Today.AddYears(1));
        await SeedIssueAsync(documentId);

        await using (var dbContext = CreateDbContext())
        {
            var ready = await CreateReadinessQuery(dbContext).GetAsync(
                new GetDocumentReadinessRequest { VesselId = VesselId },
                restrictToReadOnlyVisible: true,
                CancellationToken.None);

            var distribution = Assert.Single(ready.Items).Distribution!;
            Assert.Equal(DocumentReadinessRules.ReadyVesselStatus, ready.VesselStatus);
            Assert.Equal(1, distribution.AcknowledgedCount);
            Assert.Equal(1, distribution.PendingCount);
            Assert.Null(distribution.Recipients);
        }

        await using (var archiveContext = CreateDbContext())
        {
            var state = new DocumentRetentionState(documentId);
            state.Archive(Guid.NewGuid(), FixedUtcNow.UtcDateTime);
            await archiveContext.Set<DocumentRetentionState>().AddAsync(state);
            await archiveContext.SaveChangesAsync();
        }

        await using var verifyContext = CreateDbContext();
        var archived = await CreateReadinessQuery(verifyContext).GetAsync(
            new GetDocumentReadinessRequest { VesselId = VesselId },
            restrictToReadOnlyVisible: false,
            CancellationToken.None);

        Assert.Equal(DocumentReadinessRules.NotReadyVesselStatus, archived.VesselStatus);
        Assert.Equal([DocumentReadinessRules.InactiveOrRetained], Assert.Single(archived.Items).Blockers);
    }

    [RelationalFact]
    public async Task The_vessel_overview_is_evaluated_on_sql_server_with_the_validity_buffer_boundary()
    {
        var documentTypeId = await SeedDocumentTypeAsync();
        await SeedRequirementAsync(documentTypeId, minimumRemainingValidityDays: 30);
        var requiredThrough = Today.AddDays(60);
        await SeedApprovedDocumentAsync(documentTypeId, requiredThrough.AddDays(30));
        var unconfiguredVesselId = Guid.NewGuid();
        var directory = new FixedVesselDirectory(
            new VesselExistenceResult(VesselId, "North Star"),
            new VesselExistenceResult(unconfiguredVesselId, "South Star"));

        await using var dbContext = CreateDbContext();
        var query = new GetVesselReadinessOverviewQuery(dbContext, directory, new FixedTimeProvider(FixedUtcNow));
        var atBoundary = await query.GetAsync(
            new GetVesselReadinessOverviewRequest { RequiredThrough = requiredThrough },
            CancellationToken.None);
        var pastBoundary = await query.GetAsync(
            new GetVesselReadinessOverviewRequest { RequiredThrough = requiredThrough.AddDays(1) },
            CancellationToken.None);

        Assert.Equal(
            [
                new VesselReadinessOverviewItem(VesselId, "North Star", DocumentReadinessRules.ReadyVesselStatus, new DocumentReadinessSummary(1, 1, 0)),
                new VesselReadinessOverviewItem(unconfiguredVesselId, "South Star", DocumentReadinessRules.NotConfiguredVesselStatus, new DocumentReadinessSummary(0, 0, 0)),
            ],
            atBoundary.Items);
        Assert.Equal(DocumentReadinessRules.NotReadyVesselStatus, pastBoundary.Items[0].Status);
    }

    private async Task<bool> TryCreateAsync(HarborOpsDbContext dbContext, CreateDocumentRequirementRequest request)
    {
        try
        {
            await CreateService(dbContext).CreateAsync(Guid.NewGuid(), request, CancellationToken.None);

            return true;
        }
        catch (DuplicateDocumentRequirementException)
        {
            return false;
        }
    }

    private static UpdateDocumentRequirementRequest UpdateRequest(int days, Guid version) => new()
    {
        IsEnabled = true,
        MinimumRemainingValidityDays = days,
        Reason = "Charter update.",
        Version = version,
    };

    private async Task<int> SeedDocumentTypeAsync(string name = "Safety Management Certificate")
    {
        await using var dbContext = CreateDbContext();
        var documentType = new DocumentType(name, 365, "Flag State Authority", requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private async Task<Guid> SeedRequirementAsync(int documentTypeId, int minimumRemainingValidityDays)
    {
        await using var dbContext = CreateDbContext();
        var requirement = VesselDocumentRequirement.Create(
            VesselId, documentTypeId, minimumRemainingValidityDays, null, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        await dbContext.Set<VesselDocumentRequirement>().AddAsync(requirement);
        await dbContext.Set<VesselDocumentRequirementHistory>().AddAsync(VesselDocumentRequirementHistory.RecordCreated(requirement, "Company requirement."));
        await dbContext.SaveChangesAsync();

        return requirement.Id;
    }

    private async Task<Guid> SeedApprovedDocumentAsync(int documentTypeId, DateOnly expiryDate)
    {
        await using var dbContext = CreateDbContext();
        var document = new Document(
            VesselId,
            documentTypeId,
            $"CERT-{Guid.NewGuid():N}"[..14],
            Today.AddYears(-1),
            expiryDate,
            "Flag State Authority",
            Document.ActiveStatus);
        document.AttachFile("certificate.pdf", "application/pdf", $"key-{Guid.NewGuid():N}", 1024, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        document.SubmitForReview(Guid.NewGuid(), FixedUtcNow);
        document.Approve(Guid.NewGuid(), FixedUtcNow);
        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }

    private async Task SeedIssueAsync(Guid documentId)
    {
        await using var dbContext = CreateDbContext();
        var document = await dbContext.Set<Document>().AsNoTracking().SingleAsync(candidate => candidate.Id == documentId);
        var issue = new DocumentDistributionIssue(document, "Safety Management Certificate", Today.AddDays(7), null, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        var acknowledged = new DocumentDistributionRecipient(issue.Id, Guid.NewGuid());
        acknowledged.Acknowledge(Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        await dbContext.Set<DocumentDistributionIssue>().AddAsync(issue);
        await dbContext.Set<DocumentDistributionRecipient>().AddRangeAsync(
            acknowledged,
            new DocumentDistributionRecipient(issue.Id, Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
    }

    private static DocumentRequirementService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new KnownVesselLookup(), new FixedTimeProvider(FixedUtcNow));

    private static GetDocumentReadinessQuery CreateReadinessQuery(HarborOpsDbContext dbContext) =>
        new(dbContext, new KnownVesselLookup(), new NoRecipientLookup(), new FixedTimeProvider(FixedUtcNow));

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(HarborOpsDbContext dbContext, string sql)
    {
        var results = new List<string>();

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await dbContext.Database.OpenConnectionAsync();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!await reader.IsDBNullAsync(0))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class KnownVesselLookup : IVesselExistenceLookup
    {
        public Task<VesselExistenceResult?> GetAsync(Guid vesselId, CancellationToken cancellationToken) =>
            Task.FromResult<VesselExistenceResult?>(new VesselExistenceResult(vesselId, "North Star"));
    }

    private sealed class FixedVesselDirectory(params VesselExistenceResult[] vessels) : IVesselDirectory
    {
        public Task<VesselDirectoryPage> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new VesselDirectoryPage(vessels, vessels.Length));
    }

    private sealed class NoRecipientLookup : IDocumentDistributionRecipientLookup
    {
        public Task<IReadOnlyList<DocumentDistributionRecipientIdentity>> LookupAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentDistributionRecipientIdentity>>([]);

        public Task<IReadOnlyList<DocumentDistributionRecipientIdentity>> SearchAsync(
            string? search,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentDistributionRecipientIdentity>>([]);
    }
}
