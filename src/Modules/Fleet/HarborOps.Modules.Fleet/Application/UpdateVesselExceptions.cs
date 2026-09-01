namespace HarborOps.Modules.Fleet.Application;

internal sealed class UpdateVesselRegistryDetailsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update vessel registry details validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class UpdateVesselSpecificationsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update vessel specifications validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DuplicateMmsiException(string mmsi)
    : Exception($"A vessel with MMSI '{mmsi}' already exists.")
{
    internal string Mmsi { get; } = mmsi;
}

internal sealed class DuplicateCallSignException(string callSign)
    : Exception($"A vessel with call sign '{callSign}' already exists.")
{
    internal string CallSign { get; } = callSign;
}
