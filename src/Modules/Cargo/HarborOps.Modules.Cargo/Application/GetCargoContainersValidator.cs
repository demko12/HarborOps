using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class GetCargoContainersValidator
{
    internal const string SearchField = "search";
    internal const string StatusField = "status";
    internal const string ContainerTypeField = "containerType";
    internal const string PortOfLoadingCodeField = "portOfLoadingCode";
    internal const string PortOfDischargeCodeField = "portOfDischargeCode";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 15;
    internal const int MaximumPageSize = 15;
    internal const int MaximumPageNumber = int.MaxValue / MaximumPageSize;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetCargoContainersRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PageNumber is < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }
        else if (request.PageNumber is > MaximumPageNumber)
        {
            errors[PageNumberField] = [$"Page number must not exceed {MaximumPageNumber}."];
        }

        if (request.PageSize is < 1 or > MaximumPageSize)
        {
            errors[PageSizeField] = [$"Page size must be between 1 and {MaximumPageSize}."];
        }

        if (Normalize(request.Search) is { Length: > Container.ContainerNumberMaxLength })
        {
            errors[SearchField] = [$"Search must not exceed {Container.ContainerNumberMaxLength} characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && !TryParseStatus(request.Status, out _))
        {
            errors[StatusField] = ["Status is not a known container status."];
        }

        if (Normalize(request.ContainerType) is { } containerType
            && !ContainerEntryValidator.SupportedTypes.Contains(containerType))
        {
            errors[ContainerTypeField] = ["Container type must be a supported ISO 6346 code."];
        }

        if (!string.IsNullOrWhiteSpace(request.PortOfLoadingCode)
            && !ContainerEntryValidator.IsPortCode(request.PortOfLoadingCode))
        {
            errors[PortOfLoadingCodeField] = ["POL must be a five-character UN/LOCODE."];
        }

        if (!string.IsNullOrWhiteSpace(request.PortOfDischargeCode)
            && !ContainerEntryValidator.IsPortCode(request.PortOfDischargeCode))
        {
            errors[PortOfDischargeCodeField] = ["POD must be a five-character UN/LOCODE."];
        }

        return errors;
    }

    internal static CargoContainerManifestFilter ToFilter(GetCargoContainersRequest request)
    {
        return new CargoContainerManifestFilter(
            Normalize(request.Search),
            TryParseStatus(request.Status, out var status) ? status : null,
            Normalize(request.ContainerType),
            Normalize(request.PortOfLoadingCode),
            Normalize(request.PortOfDischargeCode));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static bool TryParseStatus(string? value, out CargoStatus status)
    {
        var name = Enum.GetNames<CargoStatus>()
            .FirstOrDefault(candidate => string.Equals(candidate, value?.Trim(), StringComparison.OrdinalIgnoreCase));

        status = name is null ? default : Enum.Parse<CargoStatus>(name);

        return name is not null;
    }
}
