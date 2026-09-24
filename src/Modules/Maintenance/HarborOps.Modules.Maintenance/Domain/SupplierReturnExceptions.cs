using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class SupplierReturnInvariantException(string reason)
    : Exception(reason)
{
}

internal sealed class SupplierReturnNotDraftException(Guid supplierReturnId, SupplierReturnStatus currentStatus)
    : Exception($"Supplier return '{supplierReturnId}' is {currentStatus} and can no longer be changed.")
{
    internal Guid SupplierReturnId { get; } = supplierReturnId;

    internal SupplierReturnStatus CurrentStatus { get; } = currentStatus;
}
