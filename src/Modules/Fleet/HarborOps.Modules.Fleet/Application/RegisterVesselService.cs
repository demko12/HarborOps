using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

public sealed class RegisterVesselService(DbContext dbContext)
{
    public async Task<RegisterVesselResponse> RegisterAsync(
        RegisterVesselRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterVesselValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterVesselValidationException(errors);
        }

        var name = request.Name!.Trim();
        var imoNumber = request.ImoNumber!.Trim();
        var flagState = request.FlagState!.Trim();
        var vesselType = request.VesselType!.Trim();
        var vessels = dbContext.Set<Vessel>();

        var imoNumberExists = await vessels
            .AsNoTracking()
            .AnyAsync(vessel => vessel.ImoNumber == imoNumber, cancellationToken);

        if (imoNumberExists)
        {
            throw new DuplicateImoNumberException(imoNumber);
        }

        var vessel = Vessel.Register(name, imoNumber, flagState, vesselType);

        await vessels.AddAsync(vessel, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterVesselResponse(vessel.Id);
    }
}
