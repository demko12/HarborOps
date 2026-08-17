using HarborOps.SharedKernel;

namespace HarborOps.UnitTests.Compliance;

internal sealed class FakeDateTimeProvider(DateTime fixedUtcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = fixedUtcNow;
}
