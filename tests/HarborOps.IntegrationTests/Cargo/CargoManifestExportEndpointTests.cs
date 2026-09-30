using System.Net;
using System.Net.Http.Json;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class CargoManifestExportEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";
    private const string CsvHeader = "Booking number,Container number,Type,POL,POD,Weight (kg),Status,Vessel ID";

    private static readonly Guid VoyageId = Guid.Parse("3c2b1a09-8f7e-4d6c-9b5a-4f3e2d1c0b9a");

    private readonly HarborOpsApiFactory _application;

    public CargoManifestExportEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Export_downloads_every_matching_container_across_pages_as_an_uncached_csv_attachment()
    {
        using var reader = await ReaderClientAsync();
        var bookingId = await RegisterBookingAsync("BK-EXP-INT-0001");
        await SeedAsync(bookingId, "EXPU", 18, CargoStatus.Loaded);
        await SeedAsync(bookingId, "EXQU", 2, CargoStatus.Registered);

        using var response = await reader.GetAsync(
            $"{ContainersEndpoint(bookingId)}/export?search=%20expu%20&status=loaded&pageNumber=2&pageSize=15",
            CancellationToken.None);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        var lines = Decode(bytes).Split("\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("manifest-BK-EXP-INT-0001-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("18", Assert.Single(response.Headers.GetValues("X-Matching-Count")));
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
        Assert.Equal(CsvHeader, lines[0]);
        Assert.Equal(
            Enumerable.Range(1, 18).Select(ordinal => $"BK-EXP-INT-0001,EXPU{ordinal:D7},42R1,NLRTM,USNYC,12500.000,Loaded,"),
            lines.Skip(1).Take(18));
        Assert.Equal(string.Empty, lines[^1]);
        Assert.Equal(20, lines.Length);
    }

    [Fact]
    public async Task Export_of_a_filter_without_matches_contains_only_the_header()
    {
        using var reader = await ReaderClientAsync();
        var bookingId = await RegisterBookingAsync("BK-EXP-INT-0002");
        await SeedAsync(bookingId, "EXRU", 2, CargoStatus.Registered);

        using var response = await reader.GetAsync(
            $"{ContainersEndpoint(bookingId)}/export?portOfDischargeCode=SGSIN",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0", Assert.Single(response.Headers.GetValues("X-Matching-Count")));
        Assert.Equal($"{CsvHeader}\r\n", Decode(await response.Content.ReadAsByteArrayAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task Print_returns_the_matching_rows_with_whole_booking_and_weight_totals()
    {
        using var reader = await ReaderClientAsync();
        var bookingId = await RegisterBookingAsync("BK-EXP-INT-0003");
        await SeedAsync(bookingId, "EXSU", 3, CargoStatus.Loaded);
        await SeedAsync(bookingId, "EXTU", 1, CargoStatus.Loaded, weightKg: null);
        await SeedAsync(bookingId, "EXVU", 2, CargoStatus.Registered);

        using var response = await reader.GetAsync(
            $"{ContainersEndpoint(bookingId)}/print?status=Loaded",
            CancellationToken.None);
        var export = await response.Content.ReadFromJsonAsync<CargoManifestExport>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(export);
        Assert.Equal("BK-EXP-INT-0003", export.BookingNumber);
        Assert.True(export.IsFiltered);
        Assert.Equal("Loaded", export.Filters.Status);
        Assert.Equal(4, export.MatchingCount);
        Assert.Equal(6, export.BookingContainerCount);
        Assert.Equal(37500m, export.KnownWeightKg);
        Assert.Equal(3, export.KnownWeightCount);
        Assert.Equal(1, export.UnknownWeightCount);
        Assert.Equal(
            ["EXSU0000001", "EXSU0000002", "EXSU0000003", "EXTU0000001"],
            export.Rows.Select(row => row.ContainerNumber));
        Assert.Equal(DateTimeKind.Utc, export.GeneratedAtUtc.Kind);
    }

    [Fact]
    public async Task Print_returns_every_matching_container_across_pages_in_number_order()
    {
        using var reader = await ReaderClientAsync();
        var bookingId = await RegisterBookingAsync("BK-EXP-INT-0011");
        await SeedAsync(bookingId, "PRPU", 18, CargoStatus.Loaded);
        await SeedAsync(bookingId, "PRQU", 2, CargoStatus.Registered);

        using var response = await reader.GetAsync(
            $"{ContainersEndpoint(bookingId)}/print?search=prpu&pageNumber=2&pageSize=15",
            CancellationToken.None);
        var export = await response.Content.ReadFromJsonAsync<CargoManifestExport>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(export);
        Assert.Equal(
            Enumerable.Range(1, 18).Select(ordinal => $"PRPU{ordinal:D7}"),
            export.Rows.Select(row => row.ContainerNumber));
        Assert.Equal(18, export.MatchingCount);
        Assert.Equal(20, export.BookingContainerCount);
    }

    [Fact]
    public async Task Print_never_includes_containers_of_another_booking()
    {
        using var reader = await ReaderClientAsync();
        var bookingId = await RegisterBookingAsync("BK-EXP-INT-0004");
        var otherBookingId = await RegisterBookingAsync("BK-EXP-INT-0005");
        await SeedAsync(bookingId, "EXWU", 1, CargoStatus.Registered);
        await SeedAsync(otherBookingId, "EXWU", 1, CargoStatus.Registered, firstOrdinal: 50);

        using var response = await reader.GetAsync(
            $"{ContainersEndpoint(bookingId)}/print?search=EXWU",
            CancellationToken.None);
        var export = await response.Content.ReadFromJsonAsync<CargoManifestExport>(CancellationToken.None);

        Assert.NotNull(export);
        Assert.Equal("EXWU0000001", Assert.Single(export.Rows).ContainerNumber);
        Assert.Equal(1, export.BookingContainerCount);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("print")]
    public async Task An_invalid_filter_is_rejected_with_a_message_for_each_field(string output)
    {
        using var reader = await ReaderClientAsync();
        var bookingId = await RegisterBookingAsync($"BK-EXP-INT-06-{output}");

        using var response = await reader.GetAsync(
            $"{ContainersEndpoint(bookingId)}/{output}?status=Lost&portOfLoadingCode=NL",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(["portOfLoadingCode", "status"], problem.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("export")]
    [InlineData("print")]
    public async Task More_matches_than_the_limit_are_rejected_with_an_actionable_message(string output)
    {
        using var reader = await ReaderClientAsync();
        var bookingId = await RegisterBookingAsync($"BK-EXP-INT-07-{output}");
        await SeedAsync(bookingId, output == "export" ? "LIMU" : "LIPU", 10_001, CargoStatus.Registered);

        using var response = await reader.GetAsync($"{ContainersEndpoint(bookingId)}/{output}", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            ["More than 10,000 containers match. Narrow the filters to export or print at most 10,000 containers."],
            problem.Errors["limit"]);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("print")]
    public async Task An_unknown_booking_is_not_found(string output)
    {
        using var reader = await ReaderClientAsync();

        using var response = await reader.GetAsync($"{ContainersEndpoint(Guid.NewGuid())}/{output}", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Cargo booking not found.", problem?.Title);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("print")]
    public async Task A_user_without_a_cargo_permission_is_forbidden(string output)
    {
        var bookingId = await RegisterBookingAsync($"BK-EXP-INT-08-{output}");
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.IdentityUsers.Read);

        using var response = await client.GetAsync($"{ContainersEndpoint(bookingId)}/{output}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("print")]
    public async Task An_anonymous_caller_is_unauthorized(string output)
    {
        var bookingId = await RegisterBookingAsync($"BK-EXP-INT-09-{output}");
        using var anonymous = _application.CreateClient();

        using var response = await anonymous.GetAsync($"{ContainersEndpoint(bookingId)}/{output}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_cargo_manager_can_also_export()
    {
        using var manager = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync("BK-EXP-INT-0010");

        using var response = await manager.GetAsync($"{ContainersEndpoint(bookingId)}/export", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpClient> ReaderClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Read);
    }

    private static string ContainersEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/containers";
    }

    private static string Decode(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    private async Task SeedAsync(
        Guid bookingId,
        string ownerCode,
        int count,
        CargoStatus status,
        decimal? weightKg = 12500m,
        int firstOrdinal = 1)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        dbContext.Set<Container>().AddRange(Enumerable
            .Range(firstOrdinal, count)
            .Select(ordinal => Container.Register(
                $"{ownerCode}{ordinal:D7}",
                "42R1",
                status,
                cargoBookingId: bookingId,
                portOfLoadingCode: "NLRTM",
                portOfDischargeCode: "USNYC",
                weightKg: weightKg)));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> RegisterBookingAsync(string bookingNumber)
    {
        using var manager = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var request = new RegisterCargoBookingRequest
        {
            BookingNumber = bookingNumber,
            VoyageId = VoyageId,
            Description = "Steel coils",
            CargoType = "General Cargo",
            WeightKg = 5000.000m,
            PackageCount = 10,
            IsHazardous = false,
        };

        using var response = await manager.PostAsJsonAsync(BookingsEndpoint, request, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCargoBookingResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }
}
