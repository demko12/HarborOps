using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class PurchaseOrderReceiptInvariantException(string reason)
    : Exception(reason)
{
}
