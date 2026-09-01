using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.UnitTests.Compliance;

public sealed class CorrectiveActionDomainTests
{
    [Fact]
    public void Record_produces_a_new_id_and_sets_all_fields_from_parameters()
    {
        var actorUserId = Guid.NewGuid();
        var evidenceDocumentId = Guid.NewGuid();
        var recordedAtUtc = new DateTimeOffset(2026, 8, 20, 14, 30, 0, TimeSpan.Zero);

        var action = CorrectiveAction.Record(
            42,
            "Replaced faulty gasket.",
            actorUserId,
            recordedAtUtc,
            evidenceDocumentId);

        Assert.NotEqual(Guid.Empty, action.Id);
        Assert.Equal(42, action.NonConformityId);
        Assert.Equal("Replaced faulty gasket.", action.ActionText);
        Assert.Equal(actorUserId, action.ActorUserId);
        Assert.Equal(recordedAtUtc, action.RecordedAtUtc);
        Assert.Equal(evidenceDocumentId, action.EvidenceDocumentId);
    }

    [Fact]
    public void Record_allows_a_null_evidence_document_id()
    {
        var action = CorrectiveAction.Record(1, "No evidence attached.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);

        Assert.Null(action.EvidenceDocumentId);
    }

    [Fact]
    public void Record_produces_distinct_ids_across_calls()
    {
        var first = CorrectiveAction.Record(1, "First.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        var second = CorrectiveAction.Record(1, "Second.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);

        Assert.NotEqual(first.Id, second.Id);
    }
}
