using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class HoldContainerService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ContainerControlDetails> HoldAsync(
        Guid containerId,
        Guid actorUserId,
        HoldContainerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ContainerControlValidator.ValidateHold(request);
        if (errors.Count > 0)
        {
            throw new ContainerControlValidationException(errors);
        }

        var container = await ContainerControlGuard.FindContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var control = await ContainerControlGuard.GetOrStartControlAsync(
            dbContext,
            containerId,
            cancellationToken);

        ContainerControlEvent entry;

        try
        {
            entry = control.Hold(
                request.Reason!,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ContainerControlTransitionNotAllowedException exception)
        {
            throw new ContainerControlTransitionException(
                containerId,
                exception.CurrentState,
                exception.RequestedAction,
                exception.Reason);
        }
        await dbContext.Set<ContainerControlEvent>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ContainerControlMapper.ToDetails(container, control);
    }
}
