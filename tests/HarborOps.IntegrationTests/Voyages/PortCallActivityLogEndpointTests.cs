using System.Net;
using System.Net.Http.Json;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class PortCallActivityLogEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public PortCallActivityLogEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    public static TheoryData<string, string, bool> Endpoints => new()
    {
        { "GET", "activity-timeline", false },
        { "GET", "activities", false },
        { "GET", "activities/{activityId}", false },
        { "POST", "activities", true },
        { "POST", "activities/{activityId}/correct", true },
        { "POST", "activities/{activityId}/void", true },
        { "GET", "statement", false },
        { "POST", "statement", true },
        { "POST", "statement/finalize", true },
        { "GET", "statement/print", false },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_action_requires_authentication_and_a_voyages_capability(
        string method,
        string path,
        bool _)
    {
        var stop = await SeedStopAsync("VOY-LOG-300", departed: true);

        using var anonymous = _application.CreateClient();
        using var unrelated = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read,
            Permissions.Fleet.Manage);

        using var anonymousResponse = await anonymous.SendAsync(Request(method, stop, path));
        using var unrelatedResponse = await unrelated.SendAsync(Request(method, stop, path));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedResponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Reads_need_voyages_read_and_changes_need_voyages_manage(
        string method,
        string path,
        bool requiresManage)
    {
        var stop = await SeedStopAsync("VOY-LOG-301", departed: true);

        using var readClient = await ReadClientAsync();
        using var response = await readClient.SendAsync(Request(method, stop, path));

        if (requiresManage)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_timestamp_without_an_explicit_offset_is_rejected_by_the_api()
    {
        var stop = await SeedStopAsync("VOY-LOG-302");
        using var client = await ManageClientAsync();

        using var response = await client.PostAsync(
            StopUri(stop, "activities"),
            new StringContent(
                """{"activityType":"AllFast","occurredAt":"2026-08-09T10:15:00"}""",
                Encoding.UTF8,
                "application/json"),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem!.Errors, error => error.Key.Contains("occurredAt", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await ActivitiesAsync());
    }

    [Fact]
    public async Task An_explicit_offset_is_stored_as_the_matching_utc_instant()
    {
        var stop = await SeedStopAsync("VOY-LOG-303");
        using var client = await ManageClientAsync();
        var occurredUtc = stop.ArrivalUtc.AddMinutes(30);
        var local = new DateTimeOffset(occurredUtc).ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-ddTHH:mm:sszzz");

        using var response = await client.PostAsync(
            StopUri(stop, "activities"),
            new StringContent(
                $$"""{"activityType":"AllFast","occurredAt":"{{local}}","notes":"First line"}""",
                Encoding.UTF8,
                "application/json"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var activity = Assert.Single(await ActivitiesAsync());
        Assert.Equal(occurredUtc, activity.OccurredAtUtc);
    }

    [Fact]
    public async Task Future_and_inverted_ranges_are_rejected_with_field_errors()
    {
        var stop = await SeedStopAsync("VOY-LOG-304");
        using var client = await ManageClientAsync();

        using var future = await client.PostAsJsonAsync(
            StopUri(stop, "activities"),
            new RecordPortCallActivityRequest
            {
                ActivityType = "AllFast",
                OccurredAt = DateTimeOffset.UtcNow.AddHours(1),
            },
            CancellationToken.None);
        using var inverted = await client.PostAsJsonAsync(
            StopUri(stop, "activities"),
            new RecordPortCallActivityRequest
            {
                ActivityType = "CargoStarted",
                OccurredAt = new DateTimeOffset(stop.ArrivalUtc.AddHours(2)),
                EndedAt = new DateTimeOffset(stop.ArrivalUtc.AddHours(1)),
            },
            CancellationToken.None);

        var futureProblem = await future.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken.None);
        var invertedProblem = await inverted.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("occurredAt", futureProblem!.Errors.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Contains("endedAt", invertedProblem!.Errors.Keys);
    }

    [Fact]
    public async Task Unknown_voyages_stops_and_activities_are_not_found_and_a_foreign_stop_is_not_accepted()
    {
        var stop = await SeedStopAsync("VOY-LOG-305");
        var other = await SeedStopAsync("VOY-LOG-306");
        using var client = await ManageClientAsync();

        using var unknownVoyage = await client.GetAsync(
            StopUri(stop with { VoyageId = Guid.NewGuid() }, "activity-timeline"),
            CancellationToken.None);
        using var foreignStop = await client.GetAsync(
            StopUri(stop with { StopId = other.StopId }, "activity-timeline"),
            CancellationToken.None);
        using var unknownActivity = await client.GetAsync(
            StopUri(stop, $"activities/{Guid.NewGuid()}"),
            CancellationToken.None);
        using var missingStatement = await client.GetAsync(StopUri(stop, "statement"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, unknownVoyage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignStop.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownActivity.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingStatement.StatusCode);
    }

    [Fact]
    public async Task A_planned_stop_rejects_activities_with_a_conflict()
    {
        var stop = await SeedStopAsync("VOY-LOG-307", arrived: false);
        using var client = await ManageClientAsync();

        using var response = await client.PostAsJsonAsync(
            StopUri(stop, "activities"),
            new RecordPortCallActivityRequest
            {
                ActivityType = "PilotOnBoard",
                OccurredAt = DateTimeOffset.UtcNow.AddHours(-1),
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Operators_record_correct_void_and_finalize_one_stable_statement()
    {
        var stop = await SeedStopAsync("VOY-LOG-308", departed: true);
        using var client = await ManageClientAsync();

        var allFast = await RecordAsync(client, stop, "AllFast", stop.ArrivalUtc.AddMinutes(-10), "Logged early");
        var duplicate = await RecordAsync(client, stop, "OperationalNote", stop.ArrivalUtc.AddHours(1), "Duplicate");
        await RecordAsync(client, stop, "CargoStarted", stop.ArrivalUtc.AddHours(1), null);

        using var created = await client.PostAsync(StopUri(stop, "statement"), null, CancellationToken.None);
        var blocked = await created.Content.ReadFromJsonAsync<PortCallStatementResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.False(blocked!.CanFinalize);
        Assert.Equal(allFast.ActivityId, Assert.Single(blocked.Blockers).ActivityId);

        using var blockedFinalize = await client.PostAsJsonAsync(
            StopUri(stop, "statement/finalize"),
            new FinalizePortCallStatementRequest { Version = blocked.Version, SourceVersion = blocked.SourceVersion },
            CancellationToken.None);
        var blockedProblem = await blockedFinalize.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, blockedFinalize.StatusCode);
        Assert.True(blockedProblem!.Extensions.ContainsKey("blockers"));

        using var corrected = await client.PostAsJsonAsync(
            StopUri(stop, $"activities/{allFast.ActivityId}/correct"),
            new CorrectPortCallActivityRequest
            {
                ActivityType = "AllFast",
                OccurredAt = new DateTimeOffset(stop.ArrivalUtc.AddMinutes(10)),
                Notes = "Deck log time",
                Reason = "Recorded before the ATA by mistake",
                Version = allFast.Version,
            },
            CancellationToken.None);
        using var voided = await client.PostAsJsonAsync(
            StopUri(stop, $"activities/{duplicate.ActivityId}/void"),
            new VoidPortCallActivityRequest { Reason = "Entered twice", Version = duplicate.Version },
            CancellationToken.None);
        using var staleVoid = await client.PostAsJsonAsync(
            StopUri(stop, $"activities/{allFast.ActivityId}/void"),
            new VoidPortCallActivityRequest { Reason = "Stale", Version = allFast.Version },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleVoid.StatusCode);

        using var readClient = await ReadClientAsync();
        var history = await readClient.GetFromJsonAsync<PortCallActivityDetailsResponse>(
            StopUri(stop, $"activities/{allFast.ActivityId}"),
            CancellationToken.None);
        Assert.Equal(["Recorded", "Corrected"], history!.History.Select(revision => revision.ChangeType));
        Assert.Equal("Logged early", history.History[0].Notes);
        Assert.Equal("Recorded before the ATA by mistake", history.History[1].Reason);
        Assert.All(history.History, revision => Assert.Contains("@", revision.ActorName, StringComparison.Ordinal));

        var timeline = await readClient.GetFromJsonAsync<PortCallTimelineResponse>(
            StopUri(stop, "activity-timeline"),
            CancellationToken.None);
        Assert.Equal(
            ["StopArrival", "ManualActivity", "ManualActivity", "StopDeparture"],
            timeline!.Entries.Select(entry => entry.Source));
        Assert.DoesNotContain(timeline.Entries, entry => entry.SourceId == duplicate.ActivityId);
        Assert.Equal(3, timeline.Activities.Count);

        var preview = await readClient.GetFromJsonAsync<PortCallStatementResponse>(
            StopUri(stop, "statement"),
            CancellationToken.None);
        Assert.True(preview!.CanFinalize);

        using var draftPrint = await readClient.GetAsync(StopUri(stop, "statement/print"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, draftPrint.StatusCode);

        using var finalize = await client.PostAsJsonAsync(
            StopUri(stop, "statement/finalize"),
            new FinalizePortCallStatementRequest { Version = preview.Version, SourceVersion = preview.SourceVersion },
            CancellationToken.None);
        var finalized = await finalize.Content.ReadFromJsonAsync<PortCallStatementResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, finalize.StatusCode);
        Assert.Equal("Finalized", finalized!.Status);
        Assert.Equal("VOY-LOG-308", finalized.VoyageNumber);
        Assert.Equal("SOF-VOY-LOG-308-01", finalized.DocumentNumber);
        Assert.Equal(history.History[0].ActorName, finalized.FinalizedByName);
        Assert.Equal(4, finalized.Entries.Count);

        using var repeat = await client.PostAsJsonAsync(
            StopUri(stop, "statement/finalize"),
            new FinalizePortCallStatementRequest { Version = finalized.Version, SourceVersion = finalized.SourceVersion },
            CancellationToken.None);
        using var lateRecord = await client.PostAsJsonAsync(
            StopUri(stop, "activities"),
            new RecordPortCallActivityRequest { ActivityType = "OperationalNote", OccurredAt = new DateTimeOffset(stop.ArrivalUtc.AddHours(2)) },
            CancellationToken.None);
        using var secondStatement = await client.PostAsync(StopUri(stop, "statement"), null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, lateRecord.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondStatement.StatusCode);

        await RenamePortAsync(stop.StopId, "Renamed port");

        var print = await readClient.GetFromJsonAsync<PortCallStatementResponse>(
            StopUri(stop, "statement/print"),
            CancellationToken.None);
        Assert.Equal(finalized.PortName, print!.PortName);
        Assert.NotEqual("Renamed port", print.PortName);
        Assert.Equal(
            finalized.Entries.Select(entry => (entry.Source, entry.Label, entry.OccurredAtUtc)),
            print.Entries.Select(entry => (entry.Source, entry.Label, entry.OccurredAtUtc)));
    }

    [Fact]
    public async Task Finalization_based_on_an_outdated_preview_is_rejected()
    {
        var stop = await SeedStopAsync("VOY-LOG-309", departed: true);
        using var client = await ManageClientAsync();

        using var created = await client.PostAsync(StopUri(stop, "statement"), null, CancellationToken.None);
        var preview = await created.Content.ReadFromJsonAsync<PortCallStatementResponse>(CancellationToken.None);

        await RecordAsync(client, stop, "CargoCompleted", stop.ArrivalUtc.AddHours(2), null);

        using var outdated = await client.PostAsJsonAsync(
            StopUri(stop, "statement/finalize"),
            new FinalizePortCallStatementRequest { Version = preview!.Version, SourceVersion = preview.SourceVersion },
            CancellationToken.None);
        using var missingVersions = await client.PostAsJsonAsync(
            StopUri(stop, "statement/finalize"),
            new FinalizePortCallStatementRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, outdated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingVersions.StatusCode);
    }

    [Fact]
    public async Task Stop_departure_and_clearance_behave_the_same_with_an_activity_log_and_no_statement()
    {
        var stop = await SeedStopAsync("VOY-LOG-310");
        using var client = await ManageClientAsync();
        await RecordAsync(client, stop, "AllFast", stop.ArrivalUtc.AddMinutes(5), null);

        using var blocked = await client.PostAsJsonAsync(
            StopUri(stop, "departure"),
            new RecordStopDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);

        await CompleteClearanceAsync(stop);

        using var departed = await client.PostAsJsonAsync(
            StopUri(stop, "departure"),
            new RecordStopDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, departed.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        Assert.False(await dbContext.Set<PortCallStatement>().AnyAsync());
        Assert.Equal(
            PortCallClearance.CompletedStatus,
            (await dbContext.Set<PortCallClearance>().SingleAsync(clearance => clearance.VoyageStopId == stop.StopId)).Status);
    }

    private Task<HttpClient> ManageClientAsync() =>
        _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read,
            Permissions.Voyages.Manage);

    private Task<HttpClient> ReadClientAsync() =>
        _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Voyages.Read);

    private static Uri StopUri(SeededStop stop, string path) =>
        new($"/api/voyages/{stop.VoyageId}/stops/{stop.StopId}/{path}", UriKind.Relative);

    private static HttpRequestMessage Request(string method, SeededStop stop, string path)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(method),
            StopUri(stop, path.Replace("{activityId}", stop.ActivityId.ToString(), StringComparison.Ordinal)));

        if (method == "POST")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task<PortCallActivityChangeResponse> RecordAsync(
        HttpClient client,
        SeededStop stop,
        string activityType,
        DateTime occurredAtUtc,
        string? notes)
    {
        using var response = await client.PostAsJsonAsync(
            StopUri(stop, "activities"),
            new RecordPortCallActivityRequest
            {
                ActivityType = activityType,
                OccurredAt = new DateTimeOffset(occurredAtUtc),
                Notes = notes,
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PortCallActivityChangeResponse>(CancellationToken.None))!;
    }

    private async Task<List<PortCallActivity>> ActivitiesAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<PortCallActivity>().AsNoTracking().ToListAsync();
    }

    private async Task RenamePortAsync(Guid stopId, string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var portId = await dbContext.Set<VoyageStop>()
            .Where(stop => stop.Id == stopId)
            .Select(stop => stop.PortId)
            .SingleAsync();
        var port = await dbContext.Set<Port>().SingleAsync(candidate => candidate.Id == portId);
        dbContext.Entry(port).Property(candidate => candidate.Name).CurrentValue = name;
        await dbContext.SaveChangesAsync();
    }

    private async Task CompleteClearanceAsync(SeededStop stop)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var clearance = await dbContext.Set<PortCallClearance>()
            .SingleAsync(candidate => candidate.VoyageStopId == stop.StopId);
        var actorId = Guid.NewGuid();
        var (item, added) = clearance.AddItem(
            PortCallClearanceCategory.Customs,
            "Customs declaration",
            true,
            null,
            null,
            null,
            actorId,
            DateTime.UtcNow);
        var cleared = clearance.ClearItem(item, "CUS-1", null, actorId, DateTime.UtcNow);
        var completed = clearance.Complete([item], actorId, DateTime.UtcNow);

        await dbContext.Set<PortCallClearanceItem>().AddAsync(item);
        await dbContext.Set<PortCallClearanceEvent>().AddRangeAsync(added, cleared, completed);
        await dbContext.SaveChangesAsync();
    }

    private async Task<SeededStop> SeedStopAsync(
        string voyageNumber,
        bool arrived = true,
        bool departed = false)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var arrivalUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-6), DateTimeKind.Utc);
        arrivalUtc = new DateTime(arrivalUtc.Ticks - (arrivalUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var departureUtc = arrivalUtc.AddHours(4);

        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MEBAR",
            "ITGOA",
            arrivalUtc.AddHours(-2),
            arrivalUtc.AddDays(2));
        voyage.Depart(arrivalUtc.AddHours(-2));

        var port = Port.Create($"Port of Bar {voyageNumber}", "Montenegro", unLocode: "MEBAR");
        var stop = VoyageStop.Create(voyage.Id, port.Id, 1, arrivalUtc, departureUtc);

        if (arrived || departed)
        {
            stop.RecordArrival(arrivalUtc);
        }

        if (departed)
        {
            stop.RecordDeparture(departureUtc);
        }

        var clearance = PortCallClearance.Create(voyage.Id, stop.Id);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.Set<PortCallClearance>().AddAsync(clearance);
        await dbContext.SaveChangesAsync();

        return new SeededStop(voyage.Id, stop.Id, arrivalUtc, Guid.NewGuid());
    }

    private sealed record SeededStop(
        Guid VoyageId,
        Guid StopId,
        DateTime ArrivalUtc,
        Guid ActivityId);
}
