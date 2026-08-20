using HarborOps.Modules.Documents.Contracts;

namespace HarborOps.UnitTests.Reports;

internal sealed class FakeReportsDocumentsExpiryQuery(ReportsDocumentsExpiryResult response) : IReportsDocumentsExpiryQuery
{
    internal int CallCount { get; private set; }

    internal ReportsDocumentsExpiryRequest? LastRequest { get; private set; }

    public Task<ReportsDocumentsExpiryResult> SearchAsync(
        ReportsDocumentsExpiryRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;

        return Task.FromResult(response);
    }
}
