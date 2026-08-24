using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RegisterBerthService(DbContext dbContext)
{
    public async Task<RegisterBerthResponse> RegisterAsync(
        RegisterBerthRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterBerthValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterBerthValidationException(errors);
        }

        var portId = request.PortId!.Value;
        var name = request.Name!.Trim();
        var normalizedName = RegisterBerthValidator.NormalizeName(name);

        var portExists = await dbContext.Set<Port>()
            .AsNoTracking()
            .AnyAsync(port => port.Id == portId, cancellationToken);

        if (!portExists)
        {
            throw new PortNotFoundException(portId);
        }

        var berths = dbContext.Set<Berth>();

        var nameExists = await berths
            .AsNoTracking()
            .AnyAsync(
                berth => berth.PortId == portId && berth.Name.ToUpper() == normalizedName,
                cancellationToken);

        if (nameExists)
        {
            throw new DuplicateBerthNameException(portId, name);
        }

        var berth = Berth.Create(
            portId,
            name,
            request.Length!.Value,
            request.MaxDraft!.Value,
            request.Status!.Trim());

        await berths.AddAsync(berth, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterBerthResponse(berth.Id);
    }
}
