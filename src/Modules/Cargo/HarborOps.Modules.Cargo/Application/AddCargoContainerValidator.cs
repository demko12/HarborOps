using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class AddCargoContainerValidator
{
    internal const string ContainerNumberField = "containerNumber";
    internal const string ContainerTypeField = "containerType";

    internal static IReadOnlyDictionary<string, string[]> Validate(AddCargoContainerRequest request)
    {
        return ContainerEntryValidator.Validate(request);
    }

    private static void ValidateContainerNumber(
        string? containerNumber,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(containerNumber))
        {
            errors[ContainerNumberField] = ["Container number is required."];
            return;
        }

        if (Container.NormalizeNumber(containerNumber).Length > Container.ContainerNumberMaxLength)
        {
            errors[ContainerNumberField] =
            [
                $"Container number must not exceed {Container.ContainerNumberMaxLength} characters.",
            ];
        }
    }

    private static void ValidateContainerType(
        string? containerType,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(containerType))
        {
            errors[ContainerTypeField] = ["Container type is required."];
            return;
        }

        if (containerType.Trim().Length > Container.ContainerTypeMaxLength)
        {
            errors[ContainerTypeField] =
            [
                $"Container type must not exceed {Container.ContainerTypeMaxLength} characters.",
            ];
        }
    }
}
