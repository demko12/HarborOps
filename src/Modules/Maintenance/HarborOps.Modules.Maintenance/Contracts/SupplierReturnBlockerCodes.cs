using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public static class SupplierReturnBlockerCodes
{
    public const string PurchaseOrderNotReceived = "PurchaseOrderNotReceived";
    public const string NoLines = "NoLines";
    public const string ExceedsReturnable = "ExceedsReturnable";
    public const string InsufficientStock = "InsufficientStock";
    public const string ReservedStock = "ReservedStock";
}
