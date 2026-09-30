using System.Data;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoManifestExportQuery(DbContext dbContext, TimeProvider timeProvider)
{
    internal const int MaximumRows = 10_000;
    internal const int MaximumReadAttempts = 8;

    private const int DeadlockVictimErrorNumber = 1205;

    public async Task<CargoManifestExport> GetAsync(
        Guid cargoBookingId,
        ExportCargoContainersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var containerRequest = new GetCargoContainersRequest
        {
            Search = request.Search,
            Status = request.Status,
            ContainerType = request.ContainerType,
            PortOfLoadingCode = request.PortOfLoadingCode,
            PortOfDischargeCode = request.PortOfDischargeCode,
        };

        var errors = GetCargoContainersValidator.Validate(containerRequest);
        if (errors.Count > 0)
        {
            throw new GetCargoContainersValidationException(errors);
        }

        var filter = GetCargoContainersValidator.ToFilter(containerRequest);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ReadAsync(cargoBookingId, filter, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == DeadlockVictimErrorNumber && attempt < MaximumReadAttempts)
            {
                // Serializable reads protect the manifest snapshot while containers are
                // being changed. Yield before retrying so a competing writer can commit.
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private async Task<CargoManifestExport> ReadAsync(
        Guid cargoBookingId,
        CargoContainerManifestFilter filter,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var containers = dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => container.CargoBookingId == cargoBookingId);

        var bookingContainerCount = await containers.CountAsync(cancellationToken);

        var rows = await filter.Apply(containers)
            .OrderBy(container => container.ContainerNumber)
            .ThenBy(container => container.Id)
            .Take(MaximumRows + 1)
            .Select(container => new CargoManifestExportRow(
                container.Id,
                container.ContainerNumber,
                container.ContainerType,
                container.Status.ToString(),
                container.PortOfLoadingCode,
                container.PortOfDischargeCode,
                container.WeightKg,
                container.VesselId))
            .ToListAsync(cancellationToken);

        var bookingNumber = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .Where(booking => booking.Id == cargoBookingId)
            .Select(booking => booking.BookingNumber)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CargoBookingNotFoundException(cargoBookingId);

        if (rows.Count > MaximumRows)
        {
            throw new CargoManifestExportLimitExceededException(MaximumRows);
        }

        var generatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var knownWeights = rows
            .Where(row => row.WeightKg is not null)
            .Select(row => row.WeightKg!.Value)
            .ToList();

        return new CargoManifestExport(
            cargoBookingId,
            bookingNumber,
            generatedAtUtc,
            new CargoManifestExportFilters(
                filter.Search,
                filter.Status?.ToString(),
                filter.ContainerType,
                filter.PortOfLoadingCode,
                filter.PortOfDischargeCode),
            !filter.IsEmpty,
            rows.Count,
            bookingContainerCount,
            knownWeights.Sum(),
            knownWeights.Count,
            rows.Count - knownWeights.Count,
            MaximumRows,
            rows);
    }
}
