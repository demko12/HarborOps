using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoDiscrepancyGuard
{
    internal static async Task<CargoBooking> FindBookingAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var bookings = dbContext.Set<CargoBooking>();
        var query = tracked ? bookings : bookings.AsNoTracking();

        return await query.SingleOrDefaultAsync(
                booking => booking.Id == cargoBookingId,
                cancellationToken)
            ?? throw new CargoBookingNotFoundException(cargoBookingId);
    }

    internal static async Task<CargoDiscrepancy> FindDiscrepancyAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        Guid discrepancyId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var discrepancies = dbContext.Set<CargoDiscrepancy>();
        var query = tracked ? discrepancies : discrepancies.AsNoTracking();

        return await query.SingleOrDefaultAsync(
                discrepancy => discrepancy.Id == discrepancyId
                    && discrepancy.CargoBookingId == cargoBookingId,
                cancellationToken)
            ?? throw new CargoDiscrepancyNotFoundException(discrepancyId);
    }

    internal static Task<int> CountInvestigationNotesAsync(
        DbContext dbContext,
        Guid discrepancyId,
        CancellationToken cancellationToken)
    {
        return dbContext
            .Set<CargoDiscrepancyEvent>()
            .AsNoTracking()
            .CountAsync(
                entry => entry.DiscrepancyId == discrepancyId
                    && entry.EventType == CargoDiscrepancyEventType.NoteAdded
                    && entry.ToStatus == CargoDiscrepancyStatus.Investigating,
                cancellationToken);
    }

    internal static async Task<ReeferExcursion?> FindSourceExcursionAsync(
        DbContext dbContext,
        CargoDiscrepancy discrepancy,
        CancellationToken cancellationToken)
    {
        if (discrepancy.SourceExcursionId is not { } excursionId)
        {
            return null;
        }

        return await dbContext
            .Set<ReeferExcursion>()
            .AsNoTracking()
            .SingleOrDefaultAsync(excursion => excursion.Id == excursionId, cancellationToken);
    }

    internal static async Task<CargoCompletionBlockers> GetCompletionBlockersAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var open = await dbContext
            .Set<CargoDiscrepancy>()
            .AsNoTracking()
            .Where(discrepancy => discrepancy.CargoBookingId == cargoBookingId
                && (discrepancy.Status == CargoDiscrepancyStatus.Reported
                    || discrepancy.Status == CargoDiscrepancyStatus.Investigating))
            .OrderBy(discrepancy => discrepancy.ReportedAtUtc)
            .ThenBy(discrepancy => discrepancy.Id)
            .Select(discrepancy => new
            {
                discrepancy.Id,
                discrepancy.Type,
                discrepancy.Severity,
                discrepancy.Status,
                discrepancy.ContainerId,
            })
            .ToListAsync(cancellationToken);

        var containerNumbers = await GetContainerNumbersAsync(
            dbContext,
            [.. open.Select(discrepancy => discrepancy.ContainerId)],
            cancellationToken);

        return new CargoCompletionBlockers(
            cargoBookingId,
            open.Count,
            open.Count(discrepancy => discrepancy.Status == CargoDiscrepancyStatus.Reported),
            open.Count(discrepancy => discrepancy.Status == CargoDiscrepancyStatus.Investigating),
            [.. open.Select(discrepancy => new CargoCompletionBlocker(
                discrepancy.Id,
                discrepancy.Type.ToString(),
                discrepancy.Severity.ToString(),
                discrepancy.Status.ToString(),
                discrepancy.ContainerId,
                ContainerNumber(containerNumbers, discrepancy.ContainerId)))]);
    }

    internal static async Task<IReadOnlyDictionary<Guid, string>> GetContainerNumbersAsync(
        DbContext dbContext,
        IReadOnlyCollection<Guid?> containerIds,
        CancellationToken cancellationToken)
    {
        var ids = containerIds
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => ids.Contains(container.Id))
            .ToDictionaryAsync(
                container => container.Id,
                container => container.ContainerNumber,
                cancellationToken);
    }

    internal static string? ContainerNumber(
        IReadOnlyDictionary<Guid, string> containerNumbers,
        Guid? containerId)
    {
        return containerId is { } id && containerNumbers.TryGetValue(id, out var number)
            ? number
            : null;
    }
}
