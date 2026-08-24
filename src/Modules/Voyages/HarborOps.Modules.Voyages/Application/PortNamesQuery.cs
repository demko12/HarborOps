using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class PortNamesQuery(DbContext dbContext) : IPortNamesQuery
{
    public async Task<PortNamesResponse> ResolveAsync(
        PortNamesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PortNamesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new PortNamesValidationException(errors);
        }

        var portIds = request.PortIds!.Distinct().ToArray();

        var ports = await dbContext.Set<Port>()
            .AsNoTracking()
            .Where(port => portIds.Contains(port.Id))
            .OrderBy(port => port.Name)
            .ThenBy(port => port.Id)
            .Select(port => new PortNameItem(port.Id, port.Name, port.Country))
            .ToListAsync(cancellationToken);

        return new PortNamesResponse(ports);
    }
}
