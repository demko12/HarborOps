using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoDiscrepanciesQuery(DbContext dbContext)
{
    public async Task<GetCargoDiscrepanciesResponse> GetAsync(
        Guid cargoBookingId,
        GetCargoDiscrepanciesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CargoDiscrepancyValidator.ValidatePage(request);
        if (errors.Count > 0)
        {
            throw new CargoDiscrepancyValidationException(errors);
        }

        await CargoDiscrepancyGuard.FindBookingAsync(dbContext, cargoBookingId, tracked: false, cancellationToken);

        var bookingDiscrepancies = dbContext
            .Set<CargoDiscrepancy>()
            .AsNoTracking()
            .Where(discrepancy => discrepancy.CargoBookingId == cargoBookingId);

        var statusCounts = await bookingDiscrepancies
            .GroupBy(discrepancy => discrepancy.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(CargoDiscrepancyStatus status) =>
            statusCounts.SingleOrDefault(entry => entry.Status == status)?.Count ?? 0;

        var summary = new CargoDiscrepancySummary(
            CountOf(CargoDiscrepancyStatus.Reported),
            CountOf(CargoDiscrepancyStatus.Investigating),
            CountOf(CargoDiscrepancyStatus.Resolved),
            CountOf(CargoDiscrepancyStatus.Voided),
            CountOf(CargoDiscrepancyStatus.Reported) + CountOf(CargoDiscrepancyStatus.Investigating));

        var filtered = bookingDiscrepancies;

        if (CargoDiscrepancyValidator.TryParse<CargoDiscrepancyStatus>(request.Status, out var status))
        {
            filtered = filtered.Where(discrepancy => discrepancy.Status == status);
        }

        if (CargoDiscrepancyValidator.TryParse<CargoDiscrepancyType>(request.Type, out var type))
        {
            filtered = filtered.Where(discrepancy => discrepancy.Type == type);
        }

        if (CargoDiscrepancyValidator.TryParse<CargoDiscrepancySeverity>(request.Severity, out var severity))
        {
            filtered = filtered.Where(discrepancy => discrepancy.Severity == severity);
        }

        if (request.ContainerId is { } containerId)
        {
            filtered = filtered.Where(discrepancy => discrepancy.ContainerId == containerId);
        }

        if (request.SourceExcursionId is { } sourceExcursionId)
        {
            filtered = filtered.Where(discrepancy => discrepancy.SourceExcursionId == sourceExcursionId);
        }

        var pageNumber = request.PageNumber ?? CargoDiscrepancyValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? CargoDiscrepancyValidator.DefaultPageSize;

        var totalCount = await filtered.CountAsync(cancellationToken);

        var page = await filtered
            .OrderByDescending(discrepancy => discrepancy.ReportedAtUtc)
            .ThenBy(discrepancy => discrepancy.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var containerNumbers = await CargoDiscrepancyGuard.GetContainerNumbersAsync(
            dbContext,
            [.. page.Select(discrepancy => discrepancy.ContainerId)],
            cancellationToken);

        return new GetCargoDiscrepanciesResponse(
            [.. page.Select(discrepancy => CargoDiscrepancyMapper.ToListItem(
                discrepancy,
                CargoDiscrepancyGuard.ContainerNumber(containerNumbers, discrepancy.ContainerId)))],
            pageNumber,
            pageSize,
            totalCount,
            summary);
    }

    public async Task<CargoDiscrepancyDetails> GetDetailsAsync(
        Guid cargoBookingId,
        Guid discrepancyId,
        CancellationToken cancellationToken)
    {
        await CargoDiscrepancyGuard.FindBookingAsync(dbContext, cargoBookingId, tracked: false, cancellationToken);

        var discrepancy = await CargoDiscrepancyGuard.FindDiscrepancyAsync(
            dbContext,
            cargoBookingId,
            discrepancyId,
            tracked: false,
            cancellationToken);

        return await LoadDetailsAsync(dbContext, discrepancy, cancellationToken);
    }

    internal static async Task<CargoDiscrepancyDetails> LoadDetailsAsync(
        DbContext dbContext,
        CargoDiscrepancy discrepancy,
        CancellationToken cancellationToken)
    {
        var booking = await CargoDiscrepancyGuard.FindBookingAsync(
            dbContext,
            discrepancy.CargoBookingId,
            tracked: false,
            cancellationToken);

        var containerNumbers = await CargoDiscrepancyGuard.GetContainerNumbersAsync(
            dbContext,
            [discrepancy.ContainerId],
            cancellationToken);

        var sourceExcursion = await CargoDiscrepancyGuard.FindSourceExcursionAsync(
            dbContext,
            discrepancy,
            cancellationToken);

        var history = await dbContext
            .Set<CargoDiscrepancyEvent>()
            .AsNoTracking()
            .Where(entry => entry.DiscrepancyId == discrepancy.Id)
            .ToListAsync(cancellationToken);

        return CargoDiscrepancyMapper.ToDetails(
            discrepancy,
            booking,
            CargoDiscrepancyGuard.ContainerNumber(containerNumbers, discrepancy.ContainerId),
            sourceExcursion,
            history);
    }
}
