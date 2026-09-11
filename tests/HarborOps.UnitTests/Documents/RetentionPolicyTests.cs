using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class RetentionPolicyTests
{
    [Fact]
    public void Constructor_sets_all_fields()
    {
        var policy = new RetentionPolicy(1, 365);

        Assert.Equal(1, policy.DocumentTypeId);
        Assert.Equal(365, policy.RetentionPeriodDays);
        Assert.NotEqual(Guid.Empty, policy.ConcurrencyToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_retention_period(int retentionPeriodDays)
    {
        Assert.Throws<ArgumentException>(() => new RetentionPolicy(1, retentionPeriodDays));
    }

    [Fact]
    public void UpdateRetentionPeriod_changes_period_and_concurrency_token()
    {
        var policy = new RetentionPolicy(1, 365);
        var tokenBefore = policy.ConcurrencyToken;

        policy.UpdateRetentionPeriod(730);

        Assert.Equal(730, policy.RetentionPeriodDays);
        Assert.NotEqual(tokenBefore, policy.ConcurrencyToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void UpdateRetentionPeriod_rejects_non_positive_retention_period(int retentionPeriodDays)
    {
        var policy = new RetentionPolicy(1, 365);

        Assert.Throws<ArgumentException>(() => policy.UpdateRetentionPeriod(retentionPeriodDays));
    }
}
