using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public static class StocktakeBlockerCodes
{
    public const string NotCounted = "NotCounted";
    public const string MissingReason = "MissingReason";
    public const string StaleBaseline = "StaleBaseline";
    public const string BelowReserved = "BelowReserved";
}
