using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class PlanCargoOperationService(DbContext dbContext)
{
    public async Task<PlanCargoOperationResponse> PlanAsync(
        Guid cargoBookingId,
        Guid responsibleUserId,
        PlanCargoOperationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PlanCargoOperationValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new PlanCargoOperationValidationException(errors);
        }

        await CargoBookingGuard.EnsureOpenAsync(dbContext, cargoBookingId, cancellationToken);

        PlanCargoOperationValidator.TryParseOperationType(
            request.OperationType,
            out var operationType);

        var operation = LoadUnloadOperation.Plan(
            cargoBookingId,
            responsibleUserId,
            operationType,
            request.PlannedAtUtc!.Value);

        await dbContext.Set<LoadUnloadOperation>().AddAsync(operation, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PlanCargoOperationResponse(
            operation.Id,
            operation.OperationType.ToString(),
            operation.State.ToString());
    }
}
