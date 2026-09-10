using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal static class PreventiveWorkDueTime
{
    internal static readonly TimeOnly EndOfDayUtc = new(23, 59, 59);

    internal static DateTime ForDueDate(DateOnly dueDate)
    {
        return dueDate.ToDateTime(EndOfDayUtc, DateTimeKind.Utc);
    }

    internal static DateOnly ToDueDate(DateTime dueUtc)
    {
        return DateOnly.FromDateTime(dueUtc);
    }
}
