using HarborOps.SharedKernel;

namespace HarborOps.IntegrationTests.Documents;

internal sealed class FakeDateTimeProvider(DateTime fixedUtcNow) : TimeProvider, IDateTimeProvider
{
    public DateTime UtcNow { get; } = fixedUtcNow;

    public override DateTimeOffset GetUtcNow() => new(UtcNow);
}
