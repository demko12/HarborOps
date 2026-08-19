namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RegisterInsurancePolicyRequest(
    Guid? VesselId,
    string? Insurer,
    string? PolicyNumber,
    string? Coverage,
    DateOnly? ExpiryDate);

public sealed record RegisterInsurancePolicyResponse(int Id);

public sealed record GetInsurancePoliciesRequest
{
    public Guid? VesselId { get; init; }

    public string? Insurer { get; init; }

    public string? Coverage { get; init; }

    public DateOnly? ExpiryFrom { get; init; }

    public DateOnly? ExpiryTo { get; init; }

    public bool ExpiredOnly { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record InsurancePolicyListItem(
    int Id,
    Guid VesselId,
    string Insurer,
    string PolicyNumber,
    string Coverage,
    DateOnly ExpiryDate,
    string ExpiryClassification);

public sealed record GetInsurancePoliciesResponse(
    IReadOnlyList<InsurancePolicyListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record InsurancePolicyOverviewItem(
    int Id,
    Guid VesselId,
    string Insurer,
    string PolicyNumber,
    string Coverage,
    DateOnly ExpiryDate,
    string ExpiryClassification);
