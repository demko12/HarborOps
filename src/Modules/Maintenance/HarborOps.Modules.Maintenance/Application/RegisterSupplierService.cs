using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class RegisterSupplierService(DbContext dbContext)
{
    public async Task<RegisterSupplierResponse> RegisterAsync(
        RegisterSupplierRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterSupplierValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterSupplierValidationException(errors);
        }

        var supplier = Supplier.Register(
            request.Name!.Trim(),
            Normalize(request.ContactPerson),
            Normalize(request.PhoneEmail),
            Normalize(request.Address),
            Normalize(request.PaymentTerms));

        await dbContext.Set<Supplier>().AddAsync(supplier, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterSupplierResponse(supplier.Id);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
