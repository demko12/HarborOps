using System.Globalization;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class GetCargoContainersValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get cargo containers validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CargoManifestExportLimitExceededException(int limit)
    : Exception(string.Create(
        CultureInfo.InvariantCulture,
        $"More than {limit:N0} containers match. Narrow the filters to export or print at most {limit:N0} containers."))
{
    internal int Limit { get; } = limit;
}
