using HarborOps.Modules.Compliance.Contracts;

namespace HarborOps.UnitTests.Reports;

internal sealed class FakeReportsComplianceExposureQuery(ReportsComplianceExposureResult response) : IReportsComplianceExposureQuery
{
    internal int CallCount { get; private set; }

    internal ReportsComplianceExposureRequest? LastRequest { get; private set; }

    public Task<ReportsComplianceExposureResult> SearchAsync(
        ReportsComplianceExposureRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;

        return Task.FromResult(response);
    }
}
