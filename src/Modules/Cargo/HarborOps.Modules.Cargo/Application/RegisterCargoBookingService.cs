using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class RegisterCargoBookingService(DbContext dbContext)
{
    public async Task<RegisterCargoBookingResponse> RegisterAsync(
        RegisterCargoBookingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterCargoBookingValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterCargoBookingValidationException(errors);
        }

        var bookingNumber = request.BookingNumber!.Trim();
        var description = request.Description!.Trim();
        var cargoType = request.CargoType!.Trim();
        var bookings = dbContext.Set<CargoBooking>();

        var bookingNumberExists = await bookings
            .AsNoTracking()
            .AnyAsync(booking => booking.BookingNumber == bookingNumber, cancellationToken);

        if (bookingNumberExists)
        {
            throw new DuplicateBookingNumberException(bookingNumber);
        }

        var booking = CargoBooking.Register(
            bookingNumber,
            request.VoyageId!.Value,
            description,
            cargoType,
            request.WeightKg!.Value,
            request.PackageCount!.Value,
            request.IsHazardous!.Value);

        await bookings.AddAsync(booking, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterCargoBookingResponse(booking.Id);
    }
}
