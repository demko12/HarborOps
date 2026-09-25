using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

internal static class DocumentRequirementTestData
{
    internal static readonly DateTimeOffset FixedUtcNow = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

    internal static readonly DateOnly Today = new(2026, 9, 24);

    internal static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    internal static async Task<DocumentType> SeedTypeAsync(HarborOpsDbContext dbContext, string name = "Safety Management Certificate")
    {
        var documentType = new DocumentType(name, 365, "Flag State Authority", requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType;
    }

    internal static async Task<VesselDocumentRequirement> SeedRequirementAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        int documentTypeId,
        int minimumRemainingValidityDays = 0,
        bool isEnabled = true)
    {
        var requirement = VesselDocumentRequirement.Create(
            vesselId,
            documentTypeId,
            minimumRemainingValidityDays,
            instruction: null,
            Guid.NewGuid(),
            FixedUtcNow.UtcDateTime);

        if (!isEnabled)
        {
            requirement.Disable(Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        }

        await dbContext.Set<VesselDocumentRequirement>().AddAsync(requirement);
        await dbContext.Set<VesselDocumentRequirementHistory>()
            .AddAsync(VesselDocumentRequirementHistory.RecordCreated(requirement, "Company requirement."));
        await dbContext.SaveChangesAsync();

        return requirement;
    }

    internal static async Task<Document> SeedDocumentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        int documentTypeId,
        DateOnly? issueDate = null,
        DateOnly? expiryDate = null,
        bool approved = true,
        bool withFile = true,
        string? number = null)
    {
        var document = new Document(
            vesselId,
            documentTypeId,
            number ?? $"CERT-{Guid.NewGuid():N}"[..14],
            issueDate ?? Today.AddYears(-1),
            expiryDate ?? Today.AddYears(2),
            "Flag State Authority",
            Document.ActiveStatus);

        if (withFile)
        {
            document.AttachFile("certificate.pdf", "application/pdf", $"key-{Guid.NewGuid():N}", 1024, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        }

        if (approved)
        {
            document.SubmitForReview(Guid.NewGuid(), FixedUtcNow);
            document.Approve(Guid.NewGuid(), FixedUtcNow);
        }

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }

    internal static async Task SetRetentionStateAsync(HarborOpsDbContext dbContext, Guid documentId, string state)
    {
        var retentionState = new DocumentRetentionState(documentId);

        switch (state)
        {
            case DocumentRetentionState.ArchivedState:
                retentionState.Archive(Guid.NewGuid(), FixedUtcNow.UtcDateTime);
                break;
            case DocumentRetentionState.PendingDispositionState:
                retentionState.MoveToPendingDisposition(FixedUtcNow.UtcDateTime);
                break;
            case DocumentRetentionState.DisposedState:
                retentionState.MoveToPendingDisposition(FixedUtcNow.UtcDateTime);
                retentionState.MarkDisposed(FixedUtcNow.UtcDateTime);
                break;
        }

        await dbContext.Set<DocumentRetentionState>().AddAsync(retentionState);
        await dbContext.SaveChangesAsync();
    }
}

internal static class DocumentDistributionTestData
{
    internal static async Task<DocumentDistributionIssue> SeedIssueAsync(
        HarborOpsDbContext dbContext,
        Document document,
        DateOnly dueDate,
        DateTime issuedAtUtc,
        params (Guid UserId, string State)[] recipients)
    {
        var issue = new DocumentDistributionIssue(document, "Safety Management Certificate", dueDate, null, Guid.NewGuid(), issuedAtUtc);
        await dbContext.Set<DocumentDistributionIssue>().AddAsync(issue);

        foreach (var (userId, state) in recipients)
        {
            var recipient = new DocumentDistributionRecipient(issue.Id, userId);

            if (state == DocumentDistributionRecipient.AcknowledgedState)
            {
                recipient.Acknowledge(userId, issuedAtUtc.AddHours(1));
            }
            else if (state == DocumentDistributionRecipient.ObsoleteState)
            {
                recipient.MarkObsolete(issuedAtUtc.AddHours(1), "Replaced.");
            }

            await dbContext.Set<DocumentDistributionRecipient>().AddAsync(recipient);
        }

        await dbContext.SaveChangesAsync();

        return issue;
    }
}

internal sealed class FakeDistributionRecipientLookup(IReadOnlyDictionary<Guid, string>? emails = null)
    : IDocumentDistributionRecipientLookup
{
    public Task<IReadOnlyList<DocumentDistributionRecipientIdentity>> LookupAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentDistributionRecipientIdentity>>(userIds
            .Where(userId => emails?.ContainsKey(userId) == true)
            .Select(userId => new DocumentDistributionRecipientIdentity(userId, emails![userId], true, true))
            .ToList());

    public Task<IReadOnlyList<DocumentDistributionRecipientIdentity>> SearchAsync(
        string? search,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentDistributionRecipientIdentity>>([]);
}

internal sealed class FakeDocumentsVesselLookup(params Guid[] knownVesselIds) : IVesselExistenceLookup
{
    public Task<VesselExistenceResult?> GetAsync(Guid vesselId, CancellationToken cancellationToken) =>
        Task.FromResult(knownVesselIds.Contains(vesselId) ? new VesselExistenceResult(vesselId, "North Star") : null);
}

internal sealed class FakeDocumentsVesselDirectory(params VesselExistenceResult[] vessels) : IVesselDirectory
{
    internal (int PageNumber, int PageSize)? LastRequest { get; private set; }

    public Task<VesselDirectoryPage> GetPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        LastRequest = (pageNumber, pageSize);

        return Task.FromResult(new VesselDirectoryPage(
            vessels.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
            vessels.Length));
    }
}
