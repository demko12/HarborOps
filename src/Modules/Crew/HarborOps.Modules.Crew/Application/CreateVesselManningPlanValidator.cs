using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class CreateVesselManningPlanValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string EffectiveStartDateField = "effectiveStartDate";
    internal const string EffectiveEndDateField = "effectiveEndDate";

    internal static IReadOnlyDictionary<string, string[]> Validate(CreateVesselManningPlanRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId is null || request.VesselId.Value == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel ID is required."];
        }

        if (request.EffectiveStartDate is null)
        {
            errors[EffectiveStartDateField] = ["Effective start date is required."];
        }

        if (request.EffectiveEndDate is null)
        {
            errors[EffectiveEndDateField] = ["Effective end date is required."];
        }
        else if (request.EffectiveStartDate is not null && request.EffectiveEndDate.Value < request.EffectiveStartDate.Value)
        {
            errors[EffectiveEndDateField] = ["Effective end date must be on or after the effective start date."];
        }

        ManningPlanLineValidation.Validate(request.Lines, errors);

        return errors;
    }
}
