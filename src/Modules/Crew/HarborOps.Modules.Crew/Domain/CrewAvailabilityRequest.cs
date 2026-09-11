namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewAvailabilityRequest
{
    internal const int TypeMaxLength = 50;
    internal const int ReasonMaxLength = 500;
    internal const int NotesMaxLength = 1000;

    internal const string RequestedStatus = "Requested";
    internal const string ApprovedStatus = "Approved";
    internal const string RejectedStatus = "Rejected";
    internal const string CancelledStatus = "Cancelled";

    internal const string ApproveAction = "approve";
    internal const string RejectAction = "reject";
    internal const string CancelAction = "cancel";

    private CrewAvailabilityRequest()
    {
    }

    private CrewAvailabilityRequest(
        Guid id,
        Guid crewMemberId,
        string type,
        string status,
        DateOnly startDate,
        DateOnly endDate,
        string reason,
        string? notes,
        Guid requestedByUserId,
        DateTime requestedAtUtc)
    {
        Id = id;
        CrewMemberId = crewMemberId;
        Type = type;
        Status = status;
        StartDate = startDate;
        EndDate = endDate;
        Reason = reason;
        Notes = notes;
        RequestedByUserId = requestedByUserId;
        RequestedAtUtc = requestedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CrewMemberId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string Reason { get; private set; } = null!;
    public string? Notes { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    internal static CrewAvailabilityRequest Create(
        Guid crewMemberId,
        string type,
        DateOnly startDate,
        DateOnly endDate,
        string reason,
        string? notes,
        Guid requestedByUserId,
        DateTime requestedAtUtc)
    {
        return new CrewAvailabilityRequest(
            Guid.NewGuid(),
            crewMemberId,
            type,
            RequestedStatus,
            startDate,
            endDate,
            reason,
            notes,
            requestedByUserId,
            requestedAtUtc);
    }

    internal void Approve(Guid reviewerUserId, DateTime reviewedAtUtc)
    {
        if (Status != RequestedStatus)
        {
            throw new InvalidCrewAvailabilityTransitionException(Id, Status, ApproveAction);
        }

        Status = ApprovedStatus;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = reviewedAtUtc;
    }

    internal void Reject(Guid reviewerUserId, string reason, DateTime reviewedAtUtc)
    {
        if (Status != RequestedStatus)
        {
            throw new InvalidCrewAvailabilityTransitionException(Id, Status, RejectAction);
        }

        Status = RejectedStatus;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = reviewedAtUtc;
        DecisionReason = reason;
    }

    internal void Cancel(Guid actorUserId, string reason, DateTime cancelledAtUtc, DateOnly today)
    {
        var cancellable = Status == RequestedStatus
            || (Status == ApprovedStatus && StartDate > today);

        if (!cancellable)
        {
            throw new InvalidCrewAvailabilityTransitionException(Id, Status, CancelAction);
        }

        Status = CancelledStatus;
        ReviewedByUserId = actorUserId;
        ReviewedAtUtc = cancelledAtUtc;
        DecisionReason = reason;
    }
}
