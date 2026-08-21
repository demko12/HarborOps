using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetSuppliersQuery(DbContext dbContext)
{
    public async Task<GetSuppliersResponse> GetSuppliersAsync(
        GetSuppliersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetSuppliersValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetSuppliersValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetSuppliersValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetSuppliersValidator.DefaultPageSize;

        var suppliers = dbContext.Set<Supplier>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            suppliers = suppliers.Where(supplier =>
                supplier.Name.Contains(search)
                || (supplier.ContactPerson != null &&
                supplier.ContactPerson.Contains(search)));
        }

        var totalCount = await suppliers.CountAsync(cancellationToken);

        var items = await suppliers
            .OrderBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(supplier => new SupplierListItem(
                supplier.Id,
                supplier.Name,
                supplier.ContactPerson,
                supplier.PhoneEmail,
                supplier.Address,
                supplier.PaymentTerms))
            .ToListAsync(cancellationToken);

        return new GetSuppliersResponse(items, pageNumber, pageSize, totalCount);
    }
}
