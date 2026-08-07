using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

public enum WorkOrderStatus
{
    Open = 0,
    InProgress = 1,
    Completed = 2,
    Canceled = 3,
}

