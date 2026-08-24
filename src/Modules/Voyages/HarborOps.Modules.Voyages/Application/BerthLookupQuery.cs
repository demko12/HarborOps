using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class BerthLookupQuery(DbContext dbContext) : IBerthLookupQuery
{
    public async Task<BerthLookupResponse> LookupAsync(
        BerthLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = BerthLookupValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new BerthLookupValidationException(errors);
        }

        var take = request.Take ?? BerthLookupValidator.DefaultTake;
        var portId = request.PortId!.Value;

        var berths = dbContext.Set<Berth>()
            .AsNoTracking()
            .Where(berth => berth.PortId == portId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            berths = berths.Where(berth => berth.Name.Contains(search));
        }

        var items = await berths
            .OrderBy(berth => berth.Name)
            .ThenBy(berth => berth.Id)
            .Take(take)
            .Select(berth => new BerthLookupItem(
                berth.Id,
                berth.PortId,
                berth.Name,
                berth.Status))
            .ToListAsync(cancellationToken);

        return new BerthLookupResponse(items);
    }
}
