namespace HarborOps.Modules.Crew.Application;

internal sealed class VesselManningPlanNotFoundException(Guid vesselManningPlanId)
    : Exception($"Vessel manning plan with ID '{vesselManningPlanId}' was not found.")
{
    internal Guid VesselManningPlanId { get; } = vesselManningPlanId;
}

internal sealed class VesselNotFoundException(Guid vesselId)
    : Exception($"Vessel with ID '{vesselId}' was not found.")
{
    internal Guid VesselId { get; } = vesselId;
}

internal sealed class OverlappingVesselManningPlanException(Guid vesselId, DateOnly startDate, DateOnly endDate)
    : Exception(
        $"Vessel '{vesselId}' already has a manning plan effective during "
        + $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}.")
{
    internal Guid VesselId { get; } = vesselId;
    internal DateOnly StartDate { get; } = startDate;
    internal DateOnly EndDate { get; } = endDate;
}

internal sealed class CreateVesselManningPlanValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Create vessel manning plan validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class ReplaceVesselManningPlanLinesValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Replace vessel manning plan lines validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class EndVesselManningPlanValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("End vessel manning plan validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetVesselManningPlansValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get vessel manning plans validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetCrewManningCoverageValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get crew manning coverage validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
