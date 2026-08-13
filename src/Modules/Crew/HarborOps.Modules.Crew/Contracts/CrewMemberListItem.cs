namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewMemberListItem(
    Guid CrewMemberId,
    string Name,
    string Nationality,
    string ImoSeafarerId,
    Guid BaseRankId,
    string BaseRankName,
    string EmploymentStatus,
    DateOnly PassportExpiry,
    DateOnly HireDate);
