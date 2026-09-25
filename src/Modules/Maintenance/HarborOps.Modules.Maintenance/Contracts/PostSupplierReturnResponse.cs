using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PostSupplierReturnResponse(
    Guid SupplierReturnId,
    string Status,
    Guid ConcurrencyToken,
    Guid PostedByUserId,
    DateTime PostedUtc,
    // True when the return had already been posted and this request changed nothing.
    bool AlreadyPosted,
    IReadOnlyList<SupplierReturnMovementItem> Movements);
