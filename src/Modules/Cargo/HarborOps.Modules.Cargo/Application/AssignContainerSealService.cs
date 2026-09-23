using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class AssignContainerSealService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ContainerControlDetails> AssignAsync(
        Guid containerId,
        Guid actorUserId,
        AssignContainerSealRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ContainerControlValidator.ValidateSeal(request);
        if (errors.Count > 0)
        {
            throw new ContainerControlValidationException(errors);
        }

        var container = await ContainerControlGuard.FindContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var sealNumber = Container.NormalizeSealNumber(request.SealNumber!);

        await ContainerControlGuard.EnsureActiveSealIsUniqueAsync(
            dbContext,
            containerId,
            sealNumber,
            cancellationToken);

        var control = await ContainerControlGuard.GetOrStartControlAsync(
            dbContext,
            containerId,
            cancellationToken);

        ContainerControlEvent entry;

        try
        {
            entry = control.AssignSeal(
                sealNumber,
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

        var hasOpenTemperatureExcursion = await ReeferMonitoringGuard.HasOpenExcursionAsync(
            dbContext,
            containerId,
            cancellationToken);

        return ContainerControlMapper.ToDetails(container, control, hasOpenTemperatureExcursion);
    }
}
