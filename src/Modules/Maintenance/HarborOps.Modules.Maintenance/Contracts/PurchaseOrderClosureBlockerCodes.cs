using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public static class PurchaseOrderClosureBlockerCodes
{
    public const string OrderNotClosable = "OrderNotClosable";
    public const string NoReceipts = "NoReceipts";
    public const string NothingOutstanding = "NothingOutstanding";
}
