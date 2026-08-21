using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RegisterSupplierRequest
{
    public string? Name { get; init; }

    public string? ContactPerson { get; init; }

    public string? PhoneEmail { get; init; }

    public string? Address { get; init; }

    public string? PaymentTerms { get; init; }
}
