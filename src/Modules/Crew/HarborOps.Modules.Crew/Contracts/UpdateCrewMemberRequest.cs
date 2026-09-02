namespace HarborOps.Modules.Crew.Contracts;

public sealed record UpdateCrewMemberRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Nationality { get; init; }
    public string? PassportNumber { get; init; }
    public DateOnly? PassportExpiry { get; init; }
    public string? CdcNumber { get; init; }
    public DateOnly? HireDate { get; init; }
    public Guid? BaseRankId { get; init; }
}
