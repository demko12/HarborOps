using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

public enum ScheduleOccurrenceState
{
    Pending = 0,
    Generated = 1,
    Completed = 2,
    Cancelled = 3,
}
