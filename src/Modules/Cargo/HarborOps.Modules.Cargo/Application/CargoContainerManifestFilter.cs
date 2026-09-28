using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal sealed record CargoContainerManifestFilter(
    string? Search,
    CargoStatus? Status,
    string? ContainerType,
    string? PortOfLoadingCode,
    string? PortOfDischargeCode)
{
    internal static CargoContainerManifestFilter None { get; } = new(null, null, null, null, null);

    internal bool IsEmpty => this == None;

    internal IQueryable<Container> Apply(IQueryable<Container> containers)
    {
        if (Search is { } search)
        {
            containers = containers.Where(container => container.ContainerNumber.ToUpper().Contains(search));
        }

        if (Status is { } status)
        {
            containers = containers.Where(container => container.Status == status);
        }

        if (ContainerType is { } containerType)
        {
            containers = containers.Where(container => container.ContainerType.ToUpper() == containerType);
        }

        if (PortOfLoadingCode is { } portOfLoadingCode)
        {
            containers = containers.Where(container =>
                container.PortOfLoadingCode != null
                && container.PortOfLoadingCode.ToUpper() == portOfLoadingCode);
        }

        if (PortOfDischargeCode is { } portOfDischargeCode)
        {
            containers = containers.Where(container =>
                container.PortOfDischargeCode != null
                && container.PortOfDischargeCode.ToUpper() == portOfDischargeCode);
        }

        return containers;
    }
}
