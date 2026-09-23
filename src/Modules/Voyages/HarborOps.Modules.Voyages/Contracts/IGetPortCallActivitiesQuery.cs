namespace HarborOps.Modules.Voyages.Contracts;

public interface IGetPortCallActivitiesQuery
{
    Task<PortCallActivitiesResponse> GetActivitiesAsync(
        Guid voyageId,
        Guid stopId,
        GetPortCallActivitiesRequest request,
        CancellationToken cancellationToken);

    Task<PortCallActivityDetailsResponse> GetActivityAsync(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        CancellationToken cancellationToken);

    Task<PortCallTimelineResponse> GetTimelineAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken);
}
