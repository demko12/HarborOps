namespace HarborOps.Modules.Notifications.Domain;

public sealed class OperationAlert
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Capability { get; private set; } = string.Empty;
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public Guid? VesselId { get; private set; }
    public Guid? CrewMemberId { get; private set; }
    public DateOnly ExpiryDate { get; private set; }
    public int ThresholdDays { get; private set; }
    public string Severity { get; private set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; private set; }
    private OperationAlert() { }
    public OperationAlert(string capability, string sourceType, Guid sourceId, Guid? vesselId, Guid? crewMemberId, DateOnly expiryDate, int thresholdDays, string severity, DateTime generatedAtUtc)
    { Capability = capability; SourceType = sourceType; SourceId = sourceId; VesselId = vesselId; CrewMemberId = crewMemberId; ExpiryDate = expiryDate; ThresholdDays = thresholdDays; Severity = severity; GeneratedAtUtc = generatedAtUtc; }
}
