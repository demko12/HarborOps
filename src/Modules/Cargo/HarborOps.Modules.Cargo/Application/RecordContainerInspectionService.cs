using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class RecordContainerInspectionService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ContainerControlDetails> RecordAsync(
        Guid containerId,
        Guid actorUserId,
        RecordContainerInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ContainerControlValidator.ValidateInspection(request);
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

        ContainerControlValidator.TryParseResult(request.Result, out var result);

        IReadOnlyList<ContainerControlEvent> entries;

        try
        {
            entries = control.RecordInspection(
                result,
                request.ConditionNotes,
                request.TemperatureCelsius,
                container.IsReefer,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ContainerTemperatureNotAllowedException)
        {
            throw new ContainerControlValidationException(
                ContainerControlValidator.TemperatureNotAllowedError());
        }

        await dbContext.Set<ContainerControlEvent>().AddRangeAsync(entries, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ContainerControlMapper.ToDetails(container, control);
    }
}
