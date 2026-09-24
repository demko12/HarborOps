using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierReturnPostingPreview(
    Guid SupplierReturnId,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanPost,
    IReadOnlyList<SupplierReturnLineItem> Lines,
    IReadOnlyList<SupplierReturnPreviewItem> Items,
    IReadOnlyList<SupplierReturnPostingBlocker> Blockers,
    DateTime GeneratedUtc);
