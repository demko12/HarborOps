namespace HarborOps.Modules.Voyages.Domain;

internal sealed record PortCallStatementBlocker(string Code, string Message, Guid? ActivityId)
{
    internal const string StopNotDepartedCode = "StopNotDeparted";
    internal const string ActualArrivalMissingCode = "ActualArrivalMissing";
    internal const string ActualDepartureMissingCode = "ActualDepartureMissing";
    internal const string ActivityOutsidePortCallCode = "ActivityOutsidePortCall";
}
