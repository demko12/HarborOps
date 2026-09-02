namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewEmploymentStatusHistory
{
    internal const int StatusMaxLength = 50;
    internal const int NotesMaxLength = 1000;

    private CrewEmploymentStatusHistory()
    {
    }

    private CrewEmploymentStatusHistory(
        Guid id,
        Guid crewMemberId,
        string status,
        Guid changedByUserId,
        string? notes,
        DateTime changedAtUtc)
    {
        Id = id;
        CrewMemberId = crewMemberId;
        Status = status;
        ChangedByUserId = changedByUserId;
        Notes = notes;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CrewMemberId { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid ChangedByUserId { get; private set; }
    public string? Notes { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }

    internal static CrewEmploymentStatusHistory Record(
        Guid crewMemberId,
        string status,
        Guid changedByUserId,
        string? notes,
        DateTime changedAtUtc)
    {
        return new CrewEmploymentStatusHistory(
            Guid.NewGuid(),
            crewMemberId,
            status,
            changedByUserId,
            notes,
            changedAtUtc);
    }
}
