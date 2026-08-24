using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class RegisterPortService(DbContext dbContext)
{
    public async Task<RegisterPortResponse> RegisterAsync(
        RegisterPortRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterPortValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterPortValidationException(errors);
        }

        var unLocode = string.IsNullOrWhiteSpace(request.UnLocode)
            ? null
            : RegisterPortValidator.NormalizeUnLocode(request.UnLocode);
        var ports = dbContext.Set<Port>();

        if (unLocode is not null)
        {
            var unLocodeExists = await ports
                .AsNoTracking()
                .AnyAsync(
                    port => port.UnLocode != null && port.UnLocode.ToUpper() == unLocode,
                    cancellationToken);

            if (unLocodeExists)
            {
                throw new DuplicateUnLocodeException(unLocode);
            }
        }

        var port = Port.Create(
            request.Name!.Trim(),
            request.Country!.Trim(),
            Normalize(request.City),
            unLocode,
            request.Latitude,
            request.Longitude,
            shipyardName: Normalize(request.ShipyardName));

        await ports.AddAsync(port, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterPortResponse(port.Id);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
