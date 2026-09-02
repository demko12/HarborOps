using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class AddCargoContainerService(DbContext dbContext)
{
    public async Task<AddCargoContainerResponse> AddAsync(
        Guid cargoBookingId,
        AddCargoContainerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = AddCargoContainerValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new AddCargoContainerValidationException(errors);
        }

        await CargoBookingGuard.EnsureOpenAsync(dbContext, cargoBookingId, cancellationToken);

        var containerNumber = Container.NormalizeNumber(request.ContainerNumber!);
        var containers = dbContext.Set<Container>();

        var containerNumberExists = await containers
            .AsNoTracking()
            .AnyAsync(container => container.ContainerNumber == containerNumber, cancellationToken);

        if (containerNumberExists)
        {
            throw new DuplicateContainerNumberException(containerNumber);
        }

        var container = Container.Register(
            containerNumber,
            request.ContainerType!.Trim(),
            CargoStatus.Registered,
            vesselId: null,
            cargoBookingId: cargoBookingId);

        await containers.AddAsync(container, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AddCargoContainerResponse(
            container.Id,
            container.ContainerNumber,
            container.Status.ToString());
    }
}
