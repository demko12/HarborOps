using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class RegisterEquipmentService(DbContext dbContext)
{
    public async Task<RegisterEquipmentResponse> RegisterAsync(
        RegisterEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterEquipmentValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterEquipmentValidationException(errors);
        }

        var equipment = Equipment.Register(
            request.VesselId!.Value,
            request.Name!.Trim(),
            request.Category!.Trim(),
            Normalize(request.Manufacturer),
            Normalize(request.SerialNumber));

        await dbContext.Set<Equipment>().AddAsync(equipment, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterEquipmentResponse(equipment.Id);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
