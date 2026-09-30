using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public static class PurchaseOrderReceiptBlockerCodes
{
    public const string OrderNotReceivable = "OrderNotReceivable";
    public const string NothingOutstanding = "NothingOutstanding";
    public const string ExceedsOutstanding = "ExceedsOutstanding";
}
