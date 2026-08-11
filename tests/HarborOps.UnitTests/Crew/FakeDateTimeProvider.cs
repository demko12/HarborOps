using HarborOps.SharedKernel;

namespace HarborOps.UnitTests.Crew;

internal sealed class FakeDateTimeProvider(DateTime fixedUtcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = fixedUtcNow;
}
