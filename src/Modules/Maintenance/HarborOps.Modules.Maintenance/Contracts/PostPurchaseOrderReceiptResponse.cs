using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PostPurchaseOrderReceiptResponse(
    PurchaseOrderReceiptDetails Receipt,
    // True when the client request id had already been posted and this request changed nothing.
    bool AlreadyPosted,
    string OrderStatus,
    Guid ConcurrencyToken,
    IReadOnlyList<string> AllowedActions);
