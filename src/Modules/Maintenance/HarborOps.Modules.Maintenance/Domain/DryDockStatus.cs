using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

public enum DryDockStatus
{
    Planned = 0,
    InProgress = 1,
    Completed = 2,
    Canceled = 3,
}
