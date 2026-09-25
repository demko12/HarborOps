using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetSupplierReturnDetailsQuery(DbContext dbContext)
{
    public async Task<SupplierReturnDetails> GetDetailsAsync(Guid supplierReturnId, CancellationToken cancellationToken)
    {
        var details = await SupplierReturnReader.ReadDetailsAsync(dbContext, supplierReturnId, cancellationToken);

        if (details is null)
        {
            throw new SupplierReturnNotFoundException(supplierReturnId);
        }

        return details;
    }
}
