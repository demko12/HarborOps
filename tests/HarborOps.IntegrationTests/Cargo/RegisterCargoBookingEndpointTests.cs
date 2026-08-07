using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Cargo.Presentation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class RegisterCargoBookingEndpointTests : IntegrationTestBase
{
    private const string Endpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("2f9d1c8b-7a6e-4d5c-8b3a-1e0f9d8c7b6a");

    private readonly HarborOpsApiFactory _application;

    public RegisterCargoBookingEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_booking()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest("BK-INT-0001");

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCargoBookingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal($"{Endpoint}/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(CargoBooking));
        var bookingNumberIndex = entityType!
            .GetIndexes()
            .Single(index =>
                index.Properties.Single().Name == nameof(CargoBooking.BookingNumber));

        Assert.Equal(request.BookingNumber, booking.BookingNumber);
        Assert.Equal(VoyageId, booking.VoyageId);
        Assert.Equal(request.Description, booking.Description);
        Assert.Equal(request.CargoType, booking.CargoType);
        Assert.Equal(request.WeightKg, booking.WeightKg);
        Assert.Equal(request.PackageCount, booking.PackageCount);
        Assert.False(booking.IsHazardous);
        Assert.Equal("CargoBookings", entityType.GetTableName());
        Assert.Equal(CargoModule.Schema, entityType.GetSchema());
        Assert.True(bookingNumberIndex.IsUnique);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest("BK-INT-0002") with
        {
            BookingNumber = string.Empty,
            VoyageId = Guid.Empty,
            WeightKg = 0m,
            PackageCount = -1,
        };

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterCargoBookingValidator.BookingNumberField, problem.Errors);
        Assert.Contains(RegisterCargoBookingValidator.VoyageIdField, problem.Errors);
        Assert.Contains(RegisterCargoBookingValidator.WeightKgField, problem.Errors);
        Assert.Contains(RegisterCargoBookingValidator.PackageCountField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_for_duplicate_booking_number()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest("BK-INT-0003");

        using var firstResponse = await client.PostAsJsonAsync(
            Endpoint,
            request,
            CancellationToken.None);
        using var duplicateResponse = await client.PostAsJsonAsync(
            Endpoint,
            request with { Description = "Different cargo" },
            CancellationToken.None);
        var problem = await duplicateResponse.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterCargoBookingValidator.BookingNumberField, problem.Errors);
    }

    [Fact]
    public async Task Module_service_and_controller_resolve_from_dependency_injection()
    {
        await using var scope = _application.Services.CreateAsyncScope();

        var service = scope.ServiceProvider.GetRequiredService<RegisterCargoBookingService>();
        var controller = ActivatorUtilities.CreateInstance<CargoBookingsController>(
            scope.ServiceProvider);

        Assert.NotNull(service);
        Assert.NotNull(controller);
    }

    private static RegisterCargoBookingRequest CreateValidRequest(string bookingNumber)
    {
        return new RegisterCargoBookingRequest
        {
            BookingNumber = bookingNumber,
            VoyageId = VoyageId,
            Description = "Palletized coffee beans",
            CargoType = "General Cargo",
            WeightKg = 12500.500m,
            PackageCount = 42,
            IsHazardous = false,
        };
    }
}
