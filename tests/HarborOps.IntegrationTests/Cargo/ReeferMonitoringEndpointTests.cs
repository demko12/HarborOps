using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class ReeferMonitoringEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";
    private const string ReeferContainerType = "42R1";

    private static readonly Guid VoyageId = Guid.Parse("3c2b1a0d-9e8f-4b7a-8c6d-5e4f3a2b1c0d");

    private static readonly DateTime MeasuredAtUtc =
        new(2026, 9, 19, 7, 45, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public ReeferMonitoringEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task GetProfile_returns_an_empty_profile_for_a_new_reefer()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddReeferAsync(client, "BK-REEF-INT-0001");

        using var response = await client.GetAsync(
            ProfileEndpoint(containerId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetReeferMonitoringProfileResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Null(result.Profile);
        Assert.Empty(result.Changes);
        Assert.Equal(0, result.OpenExcursionCount);
    }

    [Fact]
    public async Task GetProfile_is_available_to_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddReeferAsync(manageClient, "BK-REEF-INT-0002");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.GetAsync(
            ProfileEndpoint(containerId),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_is_forbidden_without_the_cargo_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            ProfileEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SaveProfile_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddReeferAsync(manageClient, "BK-REEF-INT-0003");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PutAsJsonAsync(
            ProfileEndpoint(containerId),
            ValidProfileRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RecordReading_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddReeferAsync(manageClient, "BK-REEF-INT-0004");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsJsonAsync(
            ReadingsEndpoint(containerId),
            ValidReadingRequest(1m),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ResolveExcursion_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddReeferAsync(manageClient, "BK-REEF-INT-0005");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsJsonAsync(
            $"{ReeferEndpoint(containerId)}/excursions/{Guid.NewGuid()}/resolve",
            new ResolveReeferExcursionRequest
            {
                Disposition = nameof(ReeferExcursionDisposition.AcceptedDeviation),
                Explanation = "Accepted",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SaveProfile_stores_the_thresholds_and_the_first_change()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddReeferAsync(client, "BK-REEF-INT-0006");

        using var response = await client.PutAsJsonAsync(
            ProfileEndpoint(containerId),
            ValidProfileRequest(),
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<ReeferMonitoringProfileDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(1, details.Version);
        Assert.Equal(-2m, details.MinimumCelsius);

        using var readBack = await client.GetAsync(
            ProfileEndpoint(containerId),
            CancellationToken.None);
        var profile = await readBack.Content.ReadFromJsonAsync<GetReeferMonitoringProfileResponse>(
            CancellationToken.None);

        Assert.NotNull(profile);
        Assert.NotNull(profile.Profile);
        Assert.Single(profile.Changes);
    }

    [Fact]
    public async Task SaveProfile_rejects_a_container_that_is_not_a_reefer()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-REEF-INT-0007", "40HC");

        using var response = await client.PutAsJsonAsync(
            ProfileEndpoint(containerId),
            ValidProfileRequest(),
            CancellationToken.None);
        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("containerId"));
    }

    [Fact]
    public async Task RecordReading_is_rejected_before_a_profile_exists()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddReeferAsync(client, "BK-REEF-INT-0008");

        using var response = await client.PostAsJsonAsync(
            ReadingsEndpoint(containerId),
            ValidReadingRequest(1m),
            CancellationToken.None);
        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("profile"));
    }

    [Fact]
    public async Task RecordReading_opens_an_excursion_and_blocks_the_release()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0009");

        using var response = await client.PostAsJsonAsync(
            ReadingsEndpoint(containerId),
            ValidReadingRequest(6.5m),
            CancellationToken.None);
        var recorded = await response.Content
            .ReadFromJsonAsync<RecordReeferTemperatureReadingResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(recorded);
        Assert.NotNull(recorded.Excursion);
        Assert.Equal(nameof(ReeferExcursionStatus.Open), recorded.Excursion!.Status);

        using var controlResponse = await client.GetAsync(
            $"/api/cargo/containers/{containerId}/control",
            CancellationToken.None);
        var control = await controlResponse.Content
            .ReadFromJsonAsync<GetContainerControlResponse>(CancellationToken.None);

        Assert.NotNull(control);
        Assert.False(control.Control.CanRelease);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.OpenTemperatureExcursionReason,
            control.Control.ReleaseBlockedReason);
    }

    [Fact]
    public async Task GetReadings_pages_the_history()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0010");

        foreach (var offset in new[] { -3, -1, -2 })
        {
            using var recorded = await client.PostAsJsonAsync(
                ReadingsEndpoint(containerId),
                new RecordReeferTemperatureReadingRequest
                {
                    TemperatureCelsius = 1m,
                    MeasuredAtUtc = MeasuredAtUtc.AddHours(offset),
                },
                CancellationToken.None);

            Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
        }

        using var response = await client.GetAsync(
            $"{ReadingsEndpoint(containerId)}?pageNumber=1&pageSize=2",
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetReeferTemperatureReadingsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(MeasuredAtUtc.AddHours(-1), page.Items[0].MeasuredAtUtc);
    }

    [Fact]
    public async Task GetReadings_rejects_an_oversized_page()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0011");

        using var response = await client.GetAsync(
            $"{ReadingsEndpoint(containerId)}?pageSize=1000",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CorrectReading_supersedes_the_original_and_its_excursion()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0012");
        var recorded = await RecordAsync(client, containerId, 6.5m);

        using var response = await client.PostAsJsonAsync(
            $"{ReadingsEndpoint(containerId)}/{recorded.Reading.Id}/correct",
            new CorrectReeferTemperatureReadingRequest
            {
                TemperatureCelsius = 2m,
                MeasuredAtUtc = MeasuredAtUtc,
                Reason = "Probe was misread",
            },
            CancellationToken.None);
        var corrected = await response.Content
            .ReadFromJsonAsync<RecordReeferTemperatureReadingResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(corrected);
        Assert.Null(corrected.Excursion);

        using var excursionsResponse = await client.GetAsync(
            $"{ReeferEndpoint(containerId)}/excursions",
            CancellationToken.None);
        var excursions = await excursionsResponse.Content
            .ReadFromJsonAsync<GetReeferExcursionsResponse>(CancellationToken.None);

        Assert.NotNull(excursions);
        Assert.Equal(0, excursions.OpenCount);
        Assert.Equal(
            nameof(ReeferExcursionStatus.Superseded),
            Assert.Single(excursions.Items).Status);
    }

    [Fact]
    public async Task CorrectReading_conflicts_once_the_reading_is_superseded()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0013");
        var recorded = await RecordAsync(client, containerId, 6.5m);

        var correction = new CorrectReeferTemperatureReadingRequest
        {
            TemperatureCelsius = 2m,
            MeasuredAtUtc = MeasuredAtUtc,
            Reason = "Probe was misread",
        };

        using var first = await client.PostAsJsonAsync(
            $"{ReadingsEndpoint(containerId)}/{recorded.Reading.Id}/correct",
            correction,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            $"{ReadingsEndpoint(containerId)}/{recorded.Reading.Id}/correct",
            correction,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task VoidReading_requires_a_reason()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0014");
        var recorded = await RecordAsync(client, containerId, 1m);

        using var response = await client.PostAsJsonAsync(
            $"{ReadingsEndpoint(containerId)}/{recorded.Reading.Id}/void",
            new VoidReeferTemperatureReadingRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResolveExcursion_closes_it_and_clears_the_release_blocker()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0015");
        var recorded = await RecordAsync(client, containerId, 6.5m);

        using var response = await client.PostAsJsonAsync(
            $"{ReeferEndpoint(containerId)}/excursions/{recorded.Excursion!.Id}/resolve",
            new ResolveReeferExcursionRequest
            {
                Disposition = nameof(ReeferExcursionDisposition.CorrectiveAction),
                Explanation = "Reefer unit restarted and verified",
            },
            CancellationToken.None);
        var resolved = await response.Content
            .ReadFromJsonAsync<ReeferExcursionListItem>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(resolved);
        Assert.Equal(nameof(ReeferExcursionStatus.Resolved), resolved.Status);

        using var controlResponse = await client.GetAsync(
            $"/api/cargo/containers/{containerId}/control",
            CancellationToken.None);
        var control = await controlResponse.Content
            .ReadFromJsonAsync<GetContainerControlResponse>(CancellationToken.None);

        Assert.NotNull(control);
        Assert.Equal(nameof(ContainerControlState.Pending), control.Control.State);
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
            control.Control.ReleaseBlockedReason);
    }

    [Fact]
    public async Task ResolveExcursion_conflicts_once_it_is_resolved()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0016");
        var recorded = await RecordAsync(client, containerId, 6.5m);

        var resolution = new ResolveReeferExcursionRequest
        {
            Disposition = nameof(ReeferExcursionDisposition.AcceptedDeviation),
            Explanation = "Cargo inspected and accepted",
        };

        using var first = await client.PostAsJsonAsync(
            $"{ReeferEndpoint(containerId)}/excursions/{recorded.Excursion!.Id}/resolve",
            resolution,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            $"{ReeferEndpoint(containerId)}/excursions/{recorded.Excursion.Id}/resolve",
            resolution,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task GetExcursion_returns_not_found_for_an_unknown_excursion()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddProfiledReeferAsync(client, "BK-REEF-INT-0017");

        using var response = await client.GetAsync(
            $"{ReeferEndpoint(containerId)}/excursions/{Guid.NewGuid()}",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Temperature excursion not found.", problem.Title);
    }

    [Fact]
    public async Task GetProfile_returns_not_found_for_an_unknown_container()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.GetAsync(
            ProfileEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string ReeferEndpoint(Guid containerId)
    {
        return $"/api/cargo/containers/{containerId}/reefer";
    }

    private static string ProfileEndpoint(Guid containerId)
    {
        return $"{ReeferEndpoint(containerId)}/profile";
    }

    private static string ReadingsEndpoint(Guid containerId)
    {
        return $"{ReeferEndpoint(containerId)}/readings";
    }

    private static SaveReeferMonitoringProfileRequest ValidProfileRequest()
    {
        return new SaveReeferMonitoringProfileRequest
        {
            MinimumCelsius = -2m,
            MaximumCelsius = 4m,
            TargetCelsius = 1m,
            Reason = "Initial cold chain setup",
        };
    }

    private static RecordReeferTemperatureReadingRequest ValidReadingRequest(
        decimal temperatureCelsius)
    {
        return new RecordReeferTemperatureReadingRequest
        {
            TemperatureCelsius = temperatureCelsius,
            MeasuredAtUtc = MeasuredAtUtc,
        };
    }

    private static async Task<RecordReeferTemperatureReadingResponse> RecordAsync(
        HttpClient client,
        Guid containerId,
        decimal temperatureCelsius)
    {
        using var response = await client.PostAsJsonAsync(
            ReadingsEndpoint(containerId),
            ValidReadingRequest(temperatureCelsius),
            CancellationToken.None);
        var result = await response.Content
            .ReadFromJsonAsync<RecordReeferTemperatureReadingResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result;
    }

    private static async Task<Guid> AddProfiledReeferAsync(
        HttpClient client,
        string bookingNumber)
    {
        var containerId = await AddReeferAsync(client, bookingNumber);

        using var response = await client.PutAsJsonAsync(
            ProfileEndpoint(containerId),
            ValidProfileRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return containerId;
    }

    private static Task<Guid> AddReeferAsync(HttpClient client, string bookingNumber)
    {
        return AddContainerAsync(client, bookingNumber, ReeferContainerType);
    }

    private static async Task<Guid> AddContainerAsync(
        HttpClient client,
        string bookingNumber,
        string containerType)
    {
        var bookingId = await RegisterBookingAsync(client, bookingNumber);

        using var response = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/containers",
            new AddCargoContainerRequest
            {
                ContainerNumber = "MSKU1234567",
                ContainerType = containerType,
                PortOfLoadingCode = "NLRTM",
                PortOfDischargeCode = "USNYC",
                WeightKg = 12500m,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<AddCargoContainerResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }

    private static async Task<Guid> RegisterBookingAsync(HttpClient client, string bookingNumber)
    {
        using var response = await client.PostAsJsonAsync(
            BookingsEndpoint,
            new RegisterCargoBookingRequest
            {
                BookingNumber = bookingNumber,
                VoyageId = VoyageId,
                Description = "Frozen goods",
                CargoType = "Reefer Cargo",
                WeightKg = 5000.000m,
                PackageCount = 10,
                IsHazardous = false,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCargoBookingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }

    private Task<HttpClient> CreateManageClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage,
            Permissions.Cargo.Read);
    }
}
