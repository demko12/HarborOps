using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class ConvertDefectToWorkOrderService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ConvertDefectToWorkOrderResponse> ConvertAsync(
        Guid defectId,
        ConvertDefectToWorkOrderRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ConvertDefectToWorkOrderValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ConvertDefectToWorkOrderValidationException(errors);
        }

        var defect = await dbContext.Set<VesselDefect>()
            .SingleOrDefaultAsync(candidate => candidate.Id == defectId, cancellationToken);

        if (defect is null)
        {
            throw new DefectNotFoundException(defectId);
        }

        if (defect.Priority is not { } priority)
        {
            throw new DefectTransitionNotAllowedException(defect.Id, defect.Status, DefectStatus.Converted);
        }

        var dueUtc = request.DueUtc!.Value;
        var atUtc = timeProvider.GetUtcNow().UtcDateTime;

        var workOrder = MaintenanceWorkOrder.Create(
            defect.VesselId,
            defect.Title,
            defect.Description,
            priority,
            dueUtc);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder, cancellationToken);

        defect.ConvertToWorkOrder(workOrder.Id, actorUserId, atUtc);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConvertDefectToWorkOrderResponse(defect.Id, workOrder.Id);
    }
}
