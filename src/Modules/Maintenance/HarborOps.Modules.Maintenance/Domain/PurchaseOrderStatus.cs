using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

public enum PurchaseOrderStatus
{
    Draft = 0,
    Ordered = 1,
    Received = 2,
    Cancelled = 3,
}
