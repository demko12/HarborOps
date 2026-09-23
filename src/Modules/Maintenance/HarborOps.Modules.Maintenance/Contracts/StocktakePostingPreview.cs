using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record StocktakePostingPreview(
    Guid StocktakeId,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanPost,
    IReadOnlyList<StocktakePreviewLine> Lines,
    IReadOnlyList<StocktakePostingBlocker> Blockers,
    DateTime GeneratedUtc);
