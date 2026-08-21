using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierListItem(
    Guid SupplierId,
    string Name,
    string? ContactPerson,
    string? PhoneEmail,
    string? Address,
    string? PaymentTerms);
