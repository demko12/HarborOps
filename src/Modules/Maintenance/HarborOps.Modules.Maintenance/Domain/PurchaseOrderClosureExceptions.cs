using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class PurchaseOrderClosureInvariantException(string reason)
    : Exception(reason)
{
}
