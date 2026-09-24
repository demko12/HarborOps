using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class CancelSupplierReturnService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<SupplierReturnDetails> CancelAsync(
        Guid supplierReturnId,
        CancelSupplierReturnRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CancelSupplierReturnValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new CancelSupplierReturnValidationException(errors);
        }

        var supplierReturn = await dbContext.Set<SupplierReturn>()
            .SingleOrDefaultAsync(candidate => candidate.Id == supplierReturnId, cancellationToken);

        if (supplierReturn is null)
        {
            throw new SupplierReturnNotFoundException(supplierReturnId);
        }

        supplierReturn.EnsureDraft();

        if (supplierReturn.ConcurrencyToken != request.ExpectedConcurrencyToken)
        {
            throw new SupplierReturnConcurrencyException(supplierReturnId);
        }

        supplierReturn.Cancel(request.Reason!, actorUserId, timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await SupplierReturnReader.ReadDetailsAsync(dbContext, supplierReturnId, cancellationToken))!;
    }
}
