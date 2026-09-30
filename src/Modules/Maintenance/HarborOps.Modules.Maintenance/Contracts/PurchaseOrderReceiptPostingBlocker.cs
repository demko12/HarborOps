using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderReceiptPostingBlocker(
    Guid? PurchaseOrderLineId,
    string Code,
    string Message);
