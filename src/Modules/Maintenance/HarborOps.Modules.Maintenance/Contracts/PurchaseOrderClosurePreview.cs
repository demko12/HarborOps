using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderClosurePreview(
    Guid PurchaseOrderId,
    string OrderStatus,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanClose,
    string ResultingOrderStatus,
    decimal TotalQuantityReceived,
    decimal TotalQuantityToClose,
    IReadOnlyList<PurchaseOrderClosurePreviewLine> Lines,
    IReadOnlyList<PurchaseOrderClosureBlocker> Blockers,
    DateTime GeneratedUtc);
