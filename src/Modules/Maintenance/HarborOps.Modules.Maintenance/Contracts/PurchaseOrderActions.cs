using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public static class PurchaseOrderActions
{
    public const string MarkOrdered = "MarkOrdered";
    public const string Cancel = "Cancel";
    public const string ReceivePartial = "ReceivePartial";
    public const string ReceiveRemaining = "ReceiveRemaining";
    public const string CreateReturn = "CreateReturn";
}
