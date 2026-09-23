namespace HarborOps.Modules.Cargo.Domain;

internal static class ReeferTemperature
{
    internal static bool IsWithinSupportedRange(decimal value)
    {
        return value >= ContainerControl.MinimumTemperatureCelsius
            && value <= ContainerControl.MaximumTemperatureCelsius;
    }

    internal static bool HasSupportedPrecision(decimal value)
    {
        return value == decimal.Round(value, ContainerControl.TemperatureScale);
    }
}
