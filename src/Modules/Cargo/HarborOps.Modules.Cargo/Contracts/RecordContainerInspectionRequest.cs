namespace HarborOps.Modules.Cargo.Contracts;

public sealed record RecordContainerInspectionRequest
{
    public string? Result { get; init; }

    public string? ConditionNotes { get; init; }

    public decimal? TemperatureCelsius { get; init; }
}
