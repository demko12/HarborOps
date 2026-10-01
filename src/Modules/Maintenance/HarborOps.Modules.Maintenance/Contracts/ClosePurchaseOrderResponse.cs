using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ClosePurchaseOrderResponse(
    PurchaseOrderClosureDetails Closure,
    // True when the client request id had already closed the order and this request changed nothing.
    bool AlreadyClosed,
    string OrderStatus,
    Guid ConcurrencyToken,
    IReadOnlyList<string> AllowedActions);
