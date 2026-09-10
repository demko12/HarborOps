using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GeneratePreventiveWorkResponse(
    IReadOnlyList<PreventiveWorkGenerationResultItem> Results,
    DateOnly Through,
    int CreatedCount,
    int SkippedCount,
    DateTime GeneratedUtc);
