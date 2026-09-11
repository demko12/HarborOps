using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

public enum DefectStatus
{
    Reported = 0,
    Triaged = 1,
    Converted = 2,
    Rejected = 3,
}
