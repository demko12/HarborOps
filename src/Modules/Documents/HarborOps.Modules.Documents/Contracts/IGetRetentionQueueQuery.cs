namespace HarborOps.Modules.Documents.Contracts;

public interface IGetRetentionQueueQuery
{
    Task<GetRetentionQueueResponse> GetRetentionQueueAsync(GetRetentionQueueRequest request, CancellationToken cancellationToken);
}
