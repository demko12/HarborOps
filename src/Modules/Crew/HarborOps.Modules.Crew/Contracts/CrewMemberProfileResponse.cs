namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewMemberProfileResponse(
    Guid CrewMemberId,
    string FirstName,
    string LastName,
    string Nationality,
    string ImoSeafarerId,
    string PassportNumber,
    DateOnly DateOfBirth,
    DateOnly PassportExpiry,
    string CdcNumber,
    DateOnly HireDate,
    string EmploymentStatus,
    Guid BaseRankId,
    string BaseRankName,
    IReadOnlyList<CrewCertificateProfileItem> Certificates,
    IReadOnlyList<CrewAssignmentProfileItem> Assignments);
