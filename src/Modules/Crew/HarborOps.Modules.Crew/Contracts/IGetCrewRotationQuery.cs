namespace HarborOps.Modules.Crew.Contracts;

public interface IGetCrewRotationQuery
{
    Task<CrewRotationListItem> GetAsync(Guid assignmentId, CancellationToken cancellationToken);
}
