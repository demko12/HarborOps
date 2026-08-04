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

        var companyId = request.CompanyId!.Value;
        var vesselTypeId = request.VesselTypeId!.Value;
        var name = request.Name!.Trim();
        var imoNumber = request.ImoNumber!.Trim();
        var flagState = request.FlagState!.Trim();
        var currentStatus = request.CurrentStatus!.Trim();

        var vessels = dbContext.Set<Vessel>();

        var imoNumberExists = await vessels
            .AsNoTracking()
            .AnyAsync(vessel => vessel.ImoNumber == imoNumber, cancellationToken);

        if (imoNumberExists)
        {
            throw new DuplicateImoNumberException(imoNumber);
        }

        var vesselTypeExists = await dbContext.Set<VesselType>()
            .AsNoTracking()
            .AnyAsync(vesselType => vesselType.Id == vesselTypeId, cancellationToken);

        if (!vesselTypeExists)
        {
            throw new VesselTypeNotFoundException(vesselTypeId);
        }

        var vessel = Vessel.Register(
            companyId,
            vesselTypeId,
            name,
            imoNumber,
            flagState,
            currentStatus);

        await vessels.AddAsync(vessel, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterVesselResponse(vessel.Id);
    }
}
