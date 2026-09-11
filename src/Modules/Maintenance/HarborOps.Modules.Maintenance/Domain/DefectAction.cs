using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

public enum DefectAction
{
    Triage = 0,
    Reject = 1,
    ConvertToWorkOrder = 2,
}
