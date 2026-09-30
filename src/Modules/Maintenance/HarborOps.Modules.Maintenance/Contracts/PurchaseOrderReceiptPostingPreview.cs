using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderReceiptPostingPreview(
    Guid PurchaseOrderId,
    string OrderStatus,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanPost,
    string ResultingOrderStatus,
    IReadOnlyList<PurchaseOrderReceiptPreviewLine> Lines,
    IReadOnlyList<PurchaseOrderReceiptPostingBlocker> Blockers,
    DateTime GeneratedUtc);
