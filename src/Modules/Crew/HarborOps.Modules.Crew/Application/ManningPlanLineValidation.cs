using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class ManningPlanLineValidation
{
    internal const string LinesField = "lines";

    internal static void Validate(
        IReadOnlyList<ManningPlanLineInput>? lines,
        IDictionary<string, string[]> errors)
    {
        if (lines is null || lines.Count == 0)
        {
            errors[LinesField] = ["At least one rank line is required."];
            return;
        }

        if (lines.Any(line => line.RankId is null || line.RankId.Value == Guid.Empty))
        {
            errors[LinesField] = ["Each line must specify a non-empty rankId."];
            return;
        }

        if (lines.Any(line => line.RequiredHeadcount is null || line.RequiredHeadcount.Value <= 0))
        {
            errors[LinesField] = ["Each line's required headcount must be greater than zero."];
            return;
        }

        if (lines.Select(line => line.RankId!.Value).Distinct().Count() != lines.Count)
        {
            errors[LinesField] = ["Must not contain duplicate rankId values."];
        }
    }
}
