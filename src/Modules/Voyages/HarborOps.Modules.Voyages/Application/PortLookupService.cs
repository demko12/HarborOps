using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class PortLookupService(DbContext dbContext)
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

        var portIds = request.PortIds!.Distinct().ToArray();

        var ports = await dbContext.Set<Port>()
            .AsNoTracking()
            .Where(port => portIds.Contains(port.Id))
            .Select(port => new PortLookupItem(port.Id, port.Name, port.Country))
            .ToListAsync(cancellationToken);

        return new PortLookupResponse(ports);
    }
}
