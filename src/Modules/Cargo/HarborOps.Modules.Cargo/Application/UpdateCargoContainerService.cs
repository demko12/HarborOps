using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class UpdateCargoContainerService(DbContext dbContext)
{
    public async Task<UpdateCargoContainerResponse> UpdateAsync(Guid bookingId, Guid containerId, UpdateCargoContainerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var addRequest = new AddCargoContainerRequest { ContainerNumber = request.ContainerNumber, ContainerType = request.ContainerType, PortOfLoadingCode = request.PortOfLoadingCode, PortOfDischargeCode = request.PortOfDischargeCode, WeightKg = request.WeightKg };
        var errors = ContainerEntryValidator.Validate(addRequest);
        if (errors.Count > 0) throw new AddCargoContainerValidationException(errors);
        await CargoBookingGuard.EnsureOpenAsync(dbContext, bookingId, cancellationToken);
        var container = await dbContext.Set<Container>().SingleOrDefaultAsync(item => item.Id == containerId && item.CargoBookingId == bookingId, cancellationToken) ?? throw new CargoContainerNotFoundException(containerId);
        if (container.Status != CargoStatus.Registered) throw new CargoContainerNotEditableException(containerId, container.Status);
        var number = Container.NormalizeNumber(request.ContainerNumber!);
        if (await dbContext.Set<Container>().AsNoTracking().AnyAsync(item => item.Id != containerId && item.ContainerNumber == number, cancellationToken)) throw new DuplicateContainerNumberException(number);
        var type = request.ContainerType!.Trim().ToUpperInvariant();
        var pol = ContainerEntryValidator.NormalizePort(request.PortOfLoadingCode!);
        var pod = ContainerEntryValidator.NormalizePort(request.PortOfDischargeCode!);
        var weight = request.WeightKg!.Value;
        container.Correct(number, type, pol, pod, weight);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new UpdateCargoContainerResponse(container.Id, number, type, pol, pod, weight);
    }
}

internal sealed class CargoContainerNotFoundException(Guid id) : Exception($"Container '{id}' was not found.");
internal sealed class CargoContainerNotEditableException(Guid id, CargoStatus status) : Exception($"Container '{id}' cannot be edited from status '{status}'.");
