using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Fleet.Application;

public sealed class FleetManagementService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CreateFleetResponse> CreateAsync(CreateFleetRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > Domain.Fleet.NameMaxLength || description.Length > Domain.Fleet.DescriptionMaxLength)
        {
            throw new ArgumentException("The fleet name or description is not valid.");
        }
        var normalized = name.ToUpperInvariant();
        if (await dbContext.Set<Domain.Fleet>().AnyAsync(fleet => fleet.Name.ToUpper() == normalized, cancellationToken)) throw new DuplicateFleetNameException();
        var fleet = Domain.Fleet.Register(name, description, timeProvider.GetUtcNow().UtcDateTime);
        dbContext.Add(fleet); await dbContext.SaveChangesAsync(cancellationToken);
        return new CreateFleetResponse(fleet.Id);
    }

    public async Task<AssignFleetResponse> AssignAsync(Guid vesselId, Guid fleetId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Set<Vessel>().AnyAsync(vessel => vessel.Id == vesselId, cancellationToken)) throw new FleetAssignmentVesselNotFoundException();
        if (!await dbContext.Set<Domain.Fleet>().AnyAsync(fleet => fleet.Id == fleetId, cancellationToken)) throw new FleetNotFoundException();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var active = await dbContext.Set<FleetAssignment>().SingleOrDefaultAsync(assignment => assignment.VesselId == vesselId && assignment.IsActive, cancellationToken);
        if (active?.FleetId == fleetId) throw new VesselAlreadyAssignedToFleetException();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        active?.End(now);
        var assignment = FleetAssignment.Register(vesselId, fleetId, now);
        dbContext.Add(assignment); await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return new AssignFleetResponse(assignment.Id);
    }

    public async Task EndAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Set<Vessel>().AnyAsync(vessel => vessel.Id == vesselId, cancellationToken)) throw new FleetAssignmentVesselNotFoundException();
        var active = await dbContext.Set<FleetAssignment>().SingleOrDefaultAsync(assignment => assignment.VesselId == vesselId && assignment.IsActive, cancellationToken) ?? throw new NoActiveFleetAssignmentException();
        active.End(timeProvider.GetUtcNow().UtcDateTime); await dbContext.SaveChangesAsync(cancellationToken);
    }
}
