namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed record PortCallPerformanceReportFilter
{
    public DateOnly? DepartedFrom { get; init; }
    public DateOnly? DepartedTo { get; init; }
    public Guid? VoyageId { get; init; }
    public Guid? VesselId { get; init; }
    public Guid? PortId { get; init; }
    public string? StatementAvailability { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
}
