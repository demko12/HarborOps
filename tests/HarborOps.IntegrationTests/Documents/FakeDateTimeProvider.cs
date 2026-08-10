using HarborOps.SharedKernel;

namespace HarborOps.IntegrationTests.Documents;

internal sealed class FakeDateTimeProvider(DateTime fixedUtcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = fixedUtcNow;
}
