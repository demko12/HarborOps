using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.UnitTests.Reports;

internal sealed class FakeReportsPortCallPerformanceQuery(ReportsPortCallPerformanceResult response)
    : IReportsPortCallPerformanceQuery
{
    internal int CallCount { get; private set; }

    internal ReportsPortCallPerformanceRequest? LastRequest { get; private set; }

    internal CancellationToken LastCancellationToken { get; private set; }

    public Task<ReportsPortCallPerformanceResult> SearchAsync(
        ReportsPortCallPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastCancellationToken = cancellationToken;

        return Task.FromResult(response);
    }
}
