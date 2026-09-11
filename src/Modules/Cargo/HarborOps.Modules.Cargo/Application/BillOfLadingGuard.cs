using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

internal static class BillOfLadingGuard
{
    internal static async Task<CargoBooking> FindBookingAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var booking = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == cargoBookingId, cancellationToken);

        return booking ?? throw new CargoBookingNotFoundException(cargoBookingId);
    }

    internal static async Task<BillOfLading> FindBillAsync(
        DbContext dbContext,
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        var bill = await dbContext
            .Set<BillOfLading>()
            .SingleOrDefaultAsync(candidate => candidate.Id == billOfLadingId, cancellationToken);

        return bill ?? throw new BillOfLadingNotFoundException(billOfLadingId);
    }

    internal static async Task EnsureBillNumberIsFreeAsync(
        DbContext dbContext,
        Guid billOfLadingId,
        string normalizedBillNumber,
        CancellationToken cancellationToken)
    {
        var isTaken = await dbContext
            .Set<BillOfLading>()
            .AsNoTracking()
            .AnyAsync(
                bill => bill.Id != billOfLadingId && bill.BillNumber == normalizedBillNumber,
                cancellationToken);

        if (isTaken)
        {
            throw new DuplicateBillNumberException(normalizedBillNumber);
        }
    }

    internal static async Task<IReadOnlyList<Container>> GetBookingContainersAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        return await dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => container.CargoBookingId == cargoBookingId)
            .OrderBy(container => container.ContainerNumber)
            .ToListAsync(cancellationToken);
    }

    internal static void EnsureContainersBelongToBooking(
        Guid cargoBookingId,
        IReadOnlyList<Container> bookingContainers,
        IReadOnlyList<Guid> requestedContainerIds)
    {
        var ownedContainerIds = bookingContainers
            .Select(container => container.Id)
            .ToHashSet();

        var foreignContainerIds = requestedContainerIds
            .Where(containerId => !ownedContainerIds.Contains(containerId))
            .Order()
            .ToList();

        if (foreignContainerIds.Count > 0)
        {
            throw new ContainerNotInBookingException(cargoBookingId, foreignContainerIds);
        }
    }

    internal static async Task EnsureContainersAreFreeAsync(
        DbContext dbContext,
        Guid billOfLadingId,
        IReadOnlyList<Container> bookingContainers,
        IReadOnlyList<Guid> requestedContainerIds,
        CancellationToken cancellationToken)
    {
        if (requestedContainerIds.Count == 0)
        {
            return;
        }

        var takenContainerIds = await dbContext
            .Set<BillOfLadingContainer>()
            .AsNoTracking()
            .Where(selection => selection.IsActiveSelection
                && selection.BillOfLadingId != billOfLadingId
                && requestedContainerIds.Contains(selection.ContainerId))
            .Select(selection => selection.ContainerId)
            .ToListAsync(cancellationToken);

        if (takenContainerIds.Count == 0)
        {
            return;
        }

        var takenContainerNumbers = bookingContainers
            .Where(container => takenContainerIds.Contains(container.Id))
            .Select(container => container.ContainerNumber)
            .Order(StringComparer.Ordinal)
            .ToList();

        throw new ContainerAlreadyOnBillException(takenContainerNumbers);
    }

    internal static async Task<IReadOnlyList<BillOfLadingContainer>> GetSelectionsAsync(
        DbContext dbContext,
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        return await dbContext
            .Set<BillOfLadingContainer>()
            .Where(selection => selection.BillOfLadingId == billOfLadingId)
            .ToListAsync(cancellationToken);
    }

    internal static void EnsureTotalsFitTheBooking(
        SaveBillOfLadingDraftRequest request,
        CargoBooking booking)
    {
        var errors = BillOfLadingValidator.ValidateTotalsAgainstBooking(
            request,
            booking.PackageCount,
            booking.WeightKg);

        if (errors.Count > 0)
        {
            throw new BillOfLadingValidationException(errors);
        }
    }

    internal static BillOfLadingDraftValues ToDraftValues(SaveBillOfLadingDraftRequest request)
    {
        BillOfLadingValidator.TryParseFreightTerms(request.FreightTerms, out var freightTerms);

        return new BillOfLadingDraftValues(
            request.BillNumber!,
            request.ShipperName!,
            request.ShipperAddress!,
            request.ConsigneeName!,
            request.ConsigneeAddress!,
            request.NotifyPartyName,
            request.NotifyPartyAddress,
            request.PlaceOfReceipt!,
            request.PortOfLoading!,
            request.PortOfDischarge!,
            request.PlaceOfDelivery!,
            freightTerms,
            request.CargoMarks!,
            request.CargoDescription!,
            request.PackageCount!.Value,
            request.WeightKg!.Value,
            request.IssuePlace!,
            request.RequestedIssueDate!.Value);
    }
}
