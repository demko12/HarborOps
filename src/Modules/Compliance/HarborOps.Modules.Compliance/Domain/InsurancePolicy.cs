namespace HarborOps.Modules.Compliance.Domain;

public sealed class InsurancePolicy
{
    internal const int InsurerMaxLength = 200;
    internal const int PolicyNumberMaxLength = 100;
    internal const int CoverageMaxLength = 200;

    public int Id { get; private set; }

    public Guid VesselId { get; private set; }

    public string Insurer { get; private set; } = string.Empty;

    public string PolicyNumber { get; private set; } = string.Empty;

    public string Coverage { get; private set; } = string.Empty;

    public DateOnly ExpiryDate { get; private set; }

    private InsurancePolicy()
    {
    }

    public InsurancePolicy(Guid vesselId, string insurer, string policyNumber, string coverage, DateOnly expiryDate)
    {
        VesselId = vesselId;
        Insurer = insurer;
        PolicyNumber = policyNumber;
        Coverage = coverage;
        ExpiryDate = expiryDate;
    }
}
