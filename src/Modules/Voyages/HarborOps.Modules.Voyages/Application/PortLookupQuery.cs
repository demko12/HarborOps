using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class PortLookupQuery(DbContext dbContext) : IPortLookupQuery
{
    public async Task<PortLookupResponse> LookupAsync(
        PortLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PortLookupValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new PortLookupValidationException(errors);
        }

        var take = request.Take ?? PortLookupValidator.DefaultTake;
        var ports = dbContext.Set<Port>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            ports = ports.Where(port =>
                port.Name.Contains(search)
                || (port.UnLocode != null && port.UnLocode.Contains(search)));
        }

        var items = await ports
            .OrderBy(port => port.Name)
            .ThenBy(port => port.Id)
            .Take(take)
            .Select(port => new PortLookupItem(
                port.Id,
                port.Name,
                port.Country,
                port.UnLocode))
            .ToListAsync(cancellationToken);

        return new PortLookupResponse(items);
    }
}
