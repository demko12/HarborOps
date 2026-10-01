using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class CargoDiscrepancyWorkflowService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CargoDiscrepancyDetails> StartInvestigationAsync(
        Guid cargoBookingId,
        Guid discrepancyId,
        CargoDiscrepancyActor actor,
        CancellationToken cancellationToken)
    {
        var discrepancy = await FindAsync(cargoBookingId, discrepancyId, cancellationToken);

        var entry = discrepancy.StartInvestigation(actor.UserId, actor.Name, UtcNow());

        return await SaveAsync(discrepancy, entry, cancellationToken);
    }

    public async Task<CargoDiscrepancyDetails> AddNoteAsync(
        Guid cargoBookingId,
        Guid discrepancyId,
        CargoDiscrepancyActor actor,
        AddCargoDiscrepancyNoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CargoDiscrepancyValidator.ValidateNote(request);
        if (errors.Count > 0)
        {
            throw new CargoDiscrepancyValidationException(errors);
        }

        var discrepancy = await FindAsync(cargoBookingId, discrepancyId, cancellationToken);

        var entry = discrepancy.AddNote(request.Note!, actor.UserId, actor.Name, UtcNow());

        return await SaveAsync(discrepancy, entry, cancellationToken);
    }

    public async Task<CargoDiscrepancyDetails> ResolveAsync(
        Guid cargoBookingId,
        Guid discrepancyId,
        CargoDiscrepancyActor actor,
        ResolveCargoDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CargoDiscrepancyValidator.ValidateResolution(request);
        if (errors.Count > 0)
        {
            throw new CargoDiscrepancyValidationException(errors);
        }

        var discrepancy = await FindAsync(cargoBookingId, discrepancyId, cancellationToken);

        var investigationNoteCount = await CargoDiscrepancyGuard.CountInvestigationNotesAsync(
            dbContext,
            discrepancy.Id,
            cancellationToken);

        var sourceExcursion = await CargoDiscrepancyGuard.FindSourceExcursionAsync(
            dbContext,
            discrepancy,
            cancellationToken);

        CargoDiscrepancyValidator.TryParse<CargoDiscrepancyDisposition>(request.Disposition, out var disposition);

        var entry = discrepancy.Resolve(
            disposition,
            request.Summary!,
            investigationNoteCount,
            sourceExcursion?.Status,
            actor.UserId,
            actor.Name,
            UtcNow());

        return await SaveAsync(discrepancy, entry, cancellationToken);
    }

    public async Task<CargoDiscrepancyDetails> VoidAsync(
        Guid cargoBookingId,
        Guid discrepancyId,
        CargoDiscrepancyActor actor,
        VoidCargoDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CargoDiscrepancyValidator.ValidateVoid(request);
        if (errors.Count > 0)
        {
            throw new CargoDiscrepancyValidationException(errors);
        }

        var discrepancy = await FindAsync(cargoBookingId, discrepancyId, cancellationToken);

        var entry = discrepancy.Void(request.Reason!, actor.UserId, actor.Name, UtcNow());

        return await SaveAsync(discrepancy, entry, cancellationToken);
    }

    private async Task<CargoDiscrepancy> FindAsync(
        Guid cargoBookingId,
        Guid discrepancyId,
        CancellationToken cancellationToken)
    {
        await CargoDiscrepancyGuard.FindBookingAsync(dbContext, cargoBookingId, tracked: false, cancellationToken);

        return await CargoDiscrepancyGuard.FindDiscrepancyAsync(
            dbContext,
            cargoBookingId,
            discrepancyId,
            tracked: true,
            cancellationToken);
    }

    private async Task<CargoDiscrepancyDetails> SaveAsync(
        CargoDiscrepancy discrepancy,
        CargoDiscrepancyEvent entry,
        CancellationToken cancellationToken)
    {
        await dbContext.Set<CargoDiscrepancyEvent>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetCargoDiscrepanciesQuery.LoadDetailsAsync(dbContext, discrepancy, cancellationToken);
    }

    private DateTime UtcNow()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }
}
