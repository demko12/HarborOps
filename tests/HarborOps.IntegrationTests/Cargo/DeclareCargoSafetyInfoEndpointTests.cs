using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Cargo.Presentation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class DeclareCargoSafetyInfoEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("8c7b6a5d-4e3f-4a2b-9c1d-0e9f8a7b6c5d");

    private readonly HarborOpsApiFactory _application;

    public DeclareCargoSafetyInfoEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_declares_and_persists_safety_information()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var bookingId = await RegisterBookingAsync(client, "BK-SAFE-INT-0001", isHazardous: true);

        using var response = await client.PostAsJsonAsync(
            SafetyEndpoint(bookingId),
            CreateValidRequest(),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<DeclareCargoSafetyInfoResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(SafetyEndpoint(bookingId), response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var declaration = await dbContext
            .Set<CargoSafetyInfo>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);

        Assert.Equal(bookingId, declaration.CargoBookingId);
        Assert.Equal("1.4S", declaration.ImdgClass);
        Assert.Equal("UN0336", declaration.UnNumber);
        Assert.Equal("Keep below 25 degrees Celsius", declaration.StorageConditions);
        Assert.Equal("Segregate from oxidizers", declaration.HandlingNotes);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_booking()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var unknownBookingId = Guid.Parse("1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9");

        using var response = await client.PostAsJsonAsync(
            SafetyEndpoint(unknownBookingId),
            CreateValidRequest(),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var bookingId = await RegisterBookingAsync(client, "BK-SAFE-INT-0002", isHazardous: true);
        var request = CreateValidRequest() with
        {
            ImdgClass = string.Empty,
            UnNumber = new string('B', CargoSafetyInfo.UnNumberMaxLength + 1),
            StorageConditions = "   ",
        };

        using var response = await client.PostAsJsonAsync(
            SafetyEndpoint(bookingId),
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(DeclareCargoSafetyInfoValidator.ImdgClassField, problem.Errors);
        Assert.Contains(DeclareCargoSafetyInfoValidator.UnNumberField, problem.Errors);
        Assert.Contains(DeclareCargoSafetyInfoValidator.StorageConditionsField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_for_non_hazardous_booking()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var bookingId = await RegisterBookingAsync(client, "BK-SAFE-INT-0003", isHazardous: false);

        using var response = await client.PostAsJsonAsync(
            SafetyEndpoint(bookingId),
            CreateValidRequest(),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.False(
            await dbContext
                .Set<CargoSafetyInfo>()
                .AnyAsync(
                    candidate => candidate.CargoBookingId == bookingId,
                    CancellationToken.None));
    }

    [Fact]
    public async Task Post_returns_conflict_for_duplicate_declaration()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var bookingId = await RegisterBookingAsync(client, "BK-SAFE-INT-0004", isHazardous: true);

        using var firstResponse = await client.PostAsJsonAsync(
            SafetyEndpoint(bookingId),
            CreateValidRequest(),
            CancellationToken.None);
        using var duplicateResponse = await client.PostAsJsonAsync(
            SafetyEndpoint(bookingId),
            CreateValidRequest() with { UnNumber = "UN1234" },
            CancellationToken.None);
        var problem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Equal(
            1,
            await dbContext
                .Set<CargoSafetyInfo>()
                .CountAsync(
                    candidate => candidate.CargoBookingId == bookingId,
                    CancellationToken.None));
    }

    [Fact]
    public async Task Safety_information_keeps_unique_booking_index_in_cargo_schema()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var entityType = dbContext.Model.FindEntityType(typeof(CargoSafetyInfo));
        var bookingIndex = entityType!
            .GetIndexes()
            .Single(index =>
                index.Properties.Single().Name == nameof(CargoSafetyInfo.CargoBookingId));

        Assert.Equal("CargoSafetyInfos", entityType.GetTableName());
        Assert.Equal(CargoModule.Schema, entityType.GetSchema());
        Assert.True(bookingIndex.IsUnique);
    }

    [Fact]
    public async Task Module_service_and_controller_resolve_from_dependency_injection()
    {
        await using var scope = _application.Services.CreateAsyncScope();

        var service = scope.ServiceProvider.GetRequiredService<DeclareCargoSafetyInfoService>();
        var controller = ActivatorUtilities.CreateInstance<CargoSafetyInformationController>(
            scope.ServiceProvider);

        Assert.NotNull(service);
        Assert.NotNull(controller);
    }

    private static string SafetyEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/safety-information";
    }

    private static DeclareCargoSafetyInfoRequest CreateValidRequest()
    {
        return new DeclareCargoSafetyInfoRequest
        {
            ImdgClass = " 1.4s ",
            UnNumber = " un0336 ",
            StorageConditions = " Keep below 25 degrees Celsius ",
            HandlingNotes = " Segregate from oxidizers ",
        };
    }

    private static async Task<Guid> RegisterBookingAsync(
        HttpClient client,
        string bookingNumber,
        bool isHazardous)
    {
        var request = new RegisterCargoBookingRequest
        {
            BookingNumber = bookingNumber,
            VoyageId = VoyageId,
            Description = "Ammonium nitrate",
            CargoType = "Dangerous Goods",
            WeightKg = 5000.000m,
            PackageCount = 10,
            IsHazardous = isHazardous,
        };

        using var response = await client.PostAsJsonAsync(
            BookingsEndpoint,
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCargoBookingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }
}
