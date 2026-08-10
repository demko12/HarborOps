using HarborOps.SharedKernel;

namespace HarborOps.UnitTests.Documents;

internal sealed class FakeDateTimeProvider(DateTime fixedUtcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = fixedUtcNow;
}
