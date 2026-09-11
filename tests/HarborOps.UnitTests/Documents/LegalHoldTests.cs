using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class LegalHoldTests
{
    private static readonly DateTime FixedNowUtc = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Place_sets_all_fields_and_is_active()
    {
        var documentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var hold = LegalHold.Place(documentId, "Litigation pending.", "CASE-123", actorId, FixedNowUtc);

        Assert.NotEqual(Guid.Empty, hold.Id);
        Assert.Equal(documentId, hold.DocumentId);
        Assert.Equal("Litigation pending.", hold.Reason);
        Assert.Equal("CASE-123", hold.Reference);
        Assert.Equal(actorId, hold.PlacedByUserId);
        Assert.Equal(FixedNowUtc, hold.PlacedAtUtc);
        Assert.True(hold.IsActive);
        Assert.NotEqual(Guid.Empty, hold.ConcurrencyToken);
    }

    [Fact]
    public void Place_allows_null_reference()
    {
        var hold = LegalHold.Place(Guid.NewGuid(), "Litigation pending.", null, Guid.NewGuid(), FixedNowUtc);

        Assert.Null(hold.Reference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Place_requires_non_blank_reason(string? reason)
    {
        Assert.Throws<ArgumentException>(() =>
            LegalHold.Place(Guid.NewGuid(), reason!, null, Guid.NewGuid(), FixedNowUtc));
    }

    [Fact]
    public void Release_deactivates_hold_and_changes_concurrency_token()
    {
        var hold = LegalHold.Place(Guid.NewGuid(), "Litigation pending.", null, Guid.NewGuid(), FixedNowUtc);
        var actorId = Guid.NewGuid();
        var tokenBefore = hold.ConcurrencyToken;

        hold.Release(actorId, "Litigation resolved.", FixedNowUtc);

        Assert.False(hold.IsActive);
        Assert.Equal("Litigation resolved.", hold.ReleaseReason);
        Assert.Equal(actorId, hold.ReleasedByUserId);
        Assert.Equal(FixedNowUtc, hold.ReleasedAtUtc);
        Assert.NotEqual(tokenBefore, hold.ConcurrencyToken);
    }

    [Fact]
    public void Release_when_already_released_throws()
    {
        var hold = LegalHold.Place(Guid.NewGuid(), "Litigation pending.", null, Guid.NewGuid(), FixedNowUtc);
        hold.Release(Guid.NewGuid(), "Resolved.", FixedNowUtc);

        Assert.Throws<InvalidOperationException>(() => hold.Release(Guid.NewGuid(), "Resolved again.", FixedNowUtc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Release_requires_non_blank_reason(string? reason)
    {
        var hold = LegalHold.Place(Guid.NewGuid(), "Litigation pending.", null, Guid.NewGuid(), FixedNowUtc);

        Assert.Throws<ArgumentException>(() => hold.Release(Guid.NewGuid(), reason!, FixedNowUtc));
    }
}
