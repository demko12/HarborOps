using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewMemberProfilePageTests : BunitContext
{
    private static readonly Guid MemberId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid VesselId = Guid.Parse("40000000-0000-0000-0000-000000000009");
    private static readonly Guid RankId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    private const string VesselPageJson = """
        {"items":[{"vesselId":"40000000-0000-0000-0000-000000000009","name":"Northern Star",
        "imoNumber":"5100001","flagState":"Montenegro","currentStatus":"Active",
        "vesselTypeId":"50000000-0000-0000-0000-000000000002","vesselTypeName":"Bulk carrier",
        "companyId":"60000000-0000-0000-0000-000000000001","dwt":52000,
        "updatedAtUtc":"2026-08-01T10:00:00Z"}],"pageNumber":1,"pageSize":8,"totalCount":1}
        """;

    private readonly List<(string Path, string Body)> writes = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToProfile =
        _ => Json(HttpStatusCode.OK, Profile());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPositions =
        _ => Json(HttpStatusCode.OK, Positions(Position()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRegisterCertificate =
        _ => Json(HttpStatusCode.Created, """{"id":"60000000-0000-0000-0000-000000000001"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRegisterAssignment =
        _ => Json(HttpStatusCode.Created, """{"id":"70000000-0000-0000-0000-000000000001"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDeleteAssignment =
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToQualificationEligibility =
        _ => Json(HttpStatusCode.OK, """
            {"crewMemberId":"10000000-0000-0000-0000-000000000001","rankId":"50000000-0000-0000-0000-000000000001",
            "from":"2026-01-01","to":"2026-01-01","status":"NotConfigured","missingTypes":[],"uncoveredDates":[],"certificateLinks":[]}
            """);

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToVesselSearch =
        _ => Json(HttpStatusCode.OK, VesselPageJson);

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToGetRotation =
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPlanRotation =
        _ => Json(HttpStatusCode.Created, """{"id":"80000000-0000-0000-0000-000000000001"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToUpdateRotation =
        _ => Json(HttpStatusCode.OK, """{"id":"80000000-0000-0000-0000-000000000001"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToExecuteRotation = _ => Json(
        HttpStatusCode.OK,
        """
        {"assignmentId":"31000000-0000-0000-0000-000000000001","actualSignOffDate":"2026-07-01",
        "replacementAssignmentId":"32000000-0000-0000-0000-000000000001","replacementSignOnDate":"2026-07-01",
        "replacementPlannedSignOffDate":"2026-12-31"}
        """);

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMedicalExaminations =
        _ => Json(HttpStatusCode.OK, MedicalExaminations());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRegisterMedicalExamination =
        _ => Json(HttpStatusCode.Created, """{"id":"91000000-0000-0000-0000-000000000001"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToUpdateProfile =
        _ => Json(HttpStatusCode.OK, $$"""{"id":"{{MemberId}}"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToChangeEmploymentStatus = _ => Json(
        HttpStatusCode.Created,
        $$"""
        {"id":"90000000-0000-0000-0000-000000000001","crewMemberId":"{{MemberId}}",
        "previousStatus":"Active","newStatus":"OnLeave",
        "changedByUserId":"70000000-0000-0000-0000-000000000009","notes":null,
        "changedAtUtc":"2026-08-01T10:00:00Z"}
        """);

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToEmploymentStatusHistory =
        _ => Json(HttpStatusCode.OK, """{"items":[]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMemberAvailability =
        _ => Json(HttpStatusCode.OK, """{"items":[]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToAvailabilityConflicts =
        _ => Json(HttpStatusCode.OK, """{"conflicts":[]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToAvailabilityRequest =
        _ => Json(HttpStatusCode.Created, """{"id":"90000000-0000-0000-0000-000000000009"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToAvailabilityDecision = _ => Json(
        HttpStatusCode.OK,
        """
        {"id":"90000000-0000-0000-0000-000000000009","crewMemberId":"10000000-0000-0000-0000-000000000001",
        "previousStatus":"Requested","newStatus":"Approved","actorUserId":"20000000-0000-0000-0000-000000000003",
        "reason":null,"occurredAtUtc":"2026-09-10T10:00:00Z"}
        """);

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMemberSearch = _ => Json(
        HttpStatusCode.OK,
        """
        {"items":[{"crewMemberId":"20000000-0000-0000-0000-000000000002","name":"Relief Member",
        "nationality":"Montenegro","imoSeafarerId":"IMO200002","baseRankId":"50000000-0000-0000-0000-000000000001",
        "baseRankName":"Bosun","employmentStatus":"Active","passportExpiry":"2030-01-01","hireDate":"2020-01-01"}],
        "pageNumber":1,"pageSize":10,"totalCount":1}
        """);

    // ---- Personal / employment summary ----

    [Fact]
    public void Sensitive_fields_are_masked_until_revealed()
    {
        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("P1234567", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("14 May 1990", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("CDC987654", page.Markup, StringComparison.Ordinal);
        Assert.Equal(3, page.FindAll(".crew-profile-masked").Count);

        page.Find(".crew-profile-reveal-button").Click();

        Assert.Contains("P1234567", page.Markup, StringComparison.Ordinal);
        Assert.Contains("14 May 1990", page.Markup, StringComparison.Ordinal);
        Assert.Contains("CDC987654", page.Markup, StringComparison.Ordinal);

        page.Find(".crew-profile-reveal-button").Click();

        Assert.DoesNotContain("P1234567", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_certificate_and_assignment_sections_show_their_own_empty_state()
    {
        respondToProfile = _ => Json(HttpStatusCode.OK, Profile(certificates: [], assignments: []));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("No certificates on record", page.Markup, StringComparison.Ordinal);
            Assert.Contains("No assignments on record", page.Markup, StringComparison.Ordinal);
        });
    }

    // ---- Expiry / status indicators ----

    [Theory]
    [InlineData(-10, "Expired", "crew-profile-indicator-expired")]
    [InlineData(10, "Expiring soon", "crew-profile-indicator-warning")]
    [InlineData(120, "Valid", "crew-profile-indicator-valid")]
    public void Certificate_rows_show_the_matching_expiry_indicator(int daysFromToday, string label, string cssClass)
    {
        var expiry = DateOnly.FromDateTime(DateTime.Today).AddDays(daysFromToday);
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [Certificate(expiryDate: expiry)], assignments: []));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            var indicator = page.Find(".crew-profile-card .crew-profile-indicator");
            Assert.Equal(label, indicator.TextContent.Trim());
            Assert.Contains(cssClass, indicator.ClassList);
        });
    }

    [Theory]
    [InlineData("Active", null, "Active", "crew-profile-indicator-valid")]
    [InlineData("Planned", null, "Planned", "crew-profile-indicator-warning")]
    [InlineData("Active", "2026-01-01", "Completed", "crew-profile-indicator-neutral")]
    public void Assignment_rows_show_the_matching_status_indicator(
        string status, string? actualSignOff, string label, string cssClass)
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(
                certificates: [],
                assignments: [Assignment(status: status, actualSignOffDate: actualSignOff)]));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            var indicators = page.FindAll(".crew-profile-row .crew-profile-indicator");
            var indicator = Assert.Single(indicators);
            Assert.Equal(label, indicator.TextContent.Trim());
            Assert.Contains(cssClass, indicator.ClassList);
        });
    }

    // ---- Lookup failures ----

    [Fact]
    public void A_missing_crew_member_is_reported_as_not_found()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.NotFound,
            """{"title":"Crew member not found.","status":404,"detail":"Crew member 10000000-0000-0000-0000-000000000001 was not found."}""");

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Crew member not found", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".crew-profile-summary-card"));
        });
    }

    [Fact]
    public void A_forbidden_response_explains_the_missing_permission()
    {
        respondToProfile = _ => Json(HttpStatusCode.Forbidden, """{"status":403}""");

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Crew access denied", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".crew-profile-summary-card"));
        });
    }

    [Fact]
    public void Without_the_manage_permission_the_forms_are_not_offered()
    {
        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".crew-profile-form"));
    }

    // ---- Edit profile ----

    [Fact]
    public void Without_the_manage_permission_the_edit_profile_toggle_is_not_offered()
    {
        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".crew-edit-profile-toggle"));
    }

    [Fact]
    public void The_edit_profile_toggle_opens_a_form_prefilled_with_the_current_values()
    {
        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-edit-profile-toggle")));
        page.Find(".crew-edit-profile-toggle").Click();

        Assert.Equal("Ivan", page.Find("#edit-profile-first-name").GetAttribute("value"));
        Assert.Equal("Petrović", page.Find("#edit-profile-last-name").GetAttribute("value"));
        Assert.Equal("Montenegro", page.Find("#edit-profile-nationality").GetAttribute("value"));
        Assert.Equal("P1234567", page.Find("#edit-profile-passport-number").GetAttribute("value"));
        Assert.Equal("CDC987654", page.Find("#edit-profile-cdc-number").GetAttribute("value"));

        page.Find(".crew-edit-profile-toggle").Click();

        Assert.Empty(page.FindAll("#edit-profile-first-name"));
    }

    [Fact]
    public void Updating_the_profile_refreshes_the_summary_and_closes_the_form()
    {
        var callCount = 0;
        respondToProfile = _ =>
        {
            callCount++;
            return Json(HttpStatusCode.OK, callCount == 1 ? Profile() : Profile(firstName: "Marko"));
        };

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-edit-profile-toggle")));
        page.Find(".crew-edit-profile-toggle").Click();
        page.Find("#edit-profile-first-name").Change("Marko");

        Closest(page, "#edit-profile-first-name", "form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Marko", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("#edit-profile-first-name"));
        });
    }

    [Fact]
    public void Profile_validation_problems_are_listed_and_entered_values_preserved()
    {
        respondToUpdateProfile = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"firstName":["First name is required."]}}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-edit-profile-toggle")));
        page.Find(".crew-edit-profile-toggle").Click();
        page.Find("#edit-profile-first-name").Change(string.Empty);

        Closest(page, "#edit-profile-first-name", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "First name is required.",
            page.Markup,
            StringComparison.Ordinal));
        Assert.Equal(string.Empty, page.Find("#edit-profile-first-name").GetAttribute("value"));
    }

    [Fact]
    public void A_profile_update_conflict_shows_a_banner()
    {
        respondToUpdateProfile = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Base rank not found.","status":409,"detail":"Base rank not found."}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-edit-profile-toggle")));
        page.Find(".crew-edit-profile-toggle").Click();

        Closest(page, "#edit-profile-first-name", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Base rank not found.",
            page.Find(".crew-profile-form-conflict").TextContent,
            StringComparison.Ordinal));
    }

    // ---- Employment status ----

    [Fact]
    public void Without_the_manage_permission_the_status_change_button_is_not_offered()
    {
        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".crew-employment-status-toggle"));
    }

    [Fact]
    public void Changing_employment_status_refreshes_the_badge_and_history()
    {
        var profileCallCount = 0;
        respondToProfile = _ =>
        {
            profileCallCount++;
            return Json(HttpStatusCode.OK, profileCallCount == 1 ? Profile() : Profile(employmentStatus: "OnLeave"));
        };
        respondToEmploymentStatusHistory = _ => Json(HttpStatusCode.OK, """
            {"items":[{"id":"90000000-0000-0000-0000-000000000001","status":"OnLeave",
            "changedByUserId":"70000000-0000-0000-0000-000000000009","notes":"Medical leave",
            "changedAtUtc":"2026-08-01T10:00:00Z"}]}
            """);

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-employment-status-toggle")));
        page.Find(".crew-employment-status-toggle").Click();
        page.Find("#employment-status-select").Change("OnLeave");
        page.Find("#employment-status-notes").Change("Medical leave");

        Closest(page, "#employment-status-select", "form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("OnLeave", page.Find(".employment-status").TextContent.Trim());
            Assert.Contains("employment-onleave", page.Find(".employment-status").ClassList);
            Assert.Contains("Medical leave", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_employment_status_change_conflict_shows_an_error()
    {
        respondToChangeEmploymentStatus = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"The employment status is unchanged.","status":409,"detail":"The employment status is unchanged."}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-employment-status-toggle")));
        page.Find(".crew-employment-status-toggle").Click();

        Closest(page, "#employment-status-select", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "The employment status is unchanged.",
            page.Find(".crew-profile-form-error").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Cancelling_the_status_change_hides_the_form()
    {
        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-employment-status-toggle")));
        page.Find(".crew-employment-status-toggle").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#employment-status-select")));
        page.Find(".crew-rotation-edit-toggle").Click();

        Assert.Empty(page.FindAll("#employment-status-select"));
    }

    [Fact]
    public void Employment_history_lists_recorded_changes()
    {
        respondToEmploymentStatusHistory = _ => Json(HttpStatusCode.OK, """
            {"items":[{"id":"90000000-0000-0000-0000-000000000001","status":"Suspended",
            "changedByUserId":"70000000-0000-0000-0000-000000000009","notes":"Pending review",
            "changedAtUtc":"2026-06-01T12:30:00Z"}]}
            """);

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            var card = Closest(page, "#crew-profile-employment-title", ".crew-profile-card");
            Assert.Contains("Suspended", card.TextContent, StringComparison.Ordinal);
            Assert.Contains("Pending review", card.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void No_employment_history_shows_the_empty_state()
    {
        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            var card = Closest(page, "#crew-profile-employment-title", ".crew-profile-card");
            Assert.Contains("No employment status changes on record", card.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_api_failure_loading_employment_history_shows_retry()
    {
        respondToEmploymentStatusHistory = _ => Json(HttpStatusCode.InternalServerError, """{"status":500}""");

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(
            Closest(page, "#crew-profile-employment-title", ".crew-profile-card").QuerySelectorAll(".api-error-state")));

        respondToEmploymentStatusHistory = _ => Json(HttpStatusCode.OK, """
            {"items":[{"id":"90000000-0000-0000-0000-000000000001","status":"Active",
            "changedByUserId":"70000000-0000-0000-0000-000000000009","notes":null,
            "changedAtUtc":"2026-01-01T00:00:00Z"}]}
            """);
        Closest(page, "#crew-profile-employment-title", ".crew-profile-card").QuerySelector(".api-error-state button")!.Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Active",
            Closest(page, "#crew-profile-employment-title", ".crew-profile-card").TextContent,
            StringComparison.Ordinal));
    }

    // ---- Register certificate ----

    [Fact]
    public void Registering_a_certificate_refreshes_only_the_certificates_section()
    {
        var callCount = 0;
        respondToProfile = _ =>
        {
            callCount++;
            return callCount == 1
                ? Json(HttpStatusCode.OK, Profile(
                    certificates: [Certificate(name: "Basic Safety")],
                    assignments: [Assignment(status: "Active")]))
                : Json(HttpStatusCode.OK, Profile(
                    certificates: [Certificate(name: "Advanced Firefighting")],
                    assignments: [Assignment(status: "Planned")]));
        };

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.Contains("Basic Safety", page.Markup, StringComparison.Ordinal));

        FillCertificateForm(page);
        Closest(page, "#cert-type", "form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Advanced Firefighting", page.Markup, StringComparison.Ordinal));

        // The second GetProfileAsync response also carries an updated assignments
        // list (status "Planned"), but only the certificates collection should have
        // been applied - the assignment indicator must still read "Active".
        var assignmentIndicator = page.FindAll(".crew-profile-indicator")[1];
        Assert.Equal("Active", assignmentIndicator.TextContent.Trim());
    }

    [Fact]
    public void Certificate_validation_problems_are_listed_and_entered_values_are_preserved()
    {
        respondToRegisterCertificate = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"licenseNumber":["License number must not exceed 100 characters."]}}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#cert-type")));
        FillCertificateForm(page);
        Closest(page, "#cert-type", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "License number must not exceed 100 characters.",
            page.Markup,
            StringComparison.Ordinal));
        Assert.Equal("STCW", page.Find("#cert-type").GetAttribute("value"));
        Assert.Equal("LIC-9001", page.Find("#cert-license").GetAttribute("value"));
    }

    [Fact]
    public void A_duplicate_certificate_license_shows_a_conflict_banner()
    {
        respondToRegisterCertificate = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"A certificate with this license number already exists for this crew member.","status":409,"detail":"A certificate with license number LIC-9001 already exists for this crew member."}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#cert-type")));
        FillCertificateForm(page);
        Closest(page, "#cert-type", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "A certificate with license number LIC-9001 already exists for this crew member.",
            page.Find(".crew-profile-form-conflict").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_certificate_submission_is_blocked_while_the_first_request_is_in_flight()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var postCount = 0;
        respondToRegisterCertificate = _ =>
        {
            postCount++;
            return release.Task;
        };

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#cert-type")));
        FillCertificateForm(page);
        var form = Closest(page, "#cert-type", "form");
        form.Submit();

        page.WaitForAssertion(() => Assert.True(page.Find(".crew-profile-form-submit").HasAttribute("disabled")));
        form.Submit();

        Assert.Equal(1, postCount);

        release.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                """{"id":"60000000-0000-0000-0000-000000000001"}""", Encoding.UTF8, "application/json"),
        });
        page.WaitForAssertion(() => Assert.Contains("Certificate registered.", page.Markup, StringComparison.Ordinal));
    }

    // ---- Medical fitness ----

    [Fact]
    public void Medical_history_and_current_fitness_summary_render_after_load()
    {
        respondToMedicalExaminations = _ => Json(
            HttpStatusCode.OK,
            MedicalExaminations([MedicalExamination(fitnessResult: "FitWithRestrictions")], currentFitnessResult: "FitWithRestrictions"));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            var card = page.Find(".crew-medical-card");
            Assert.Contains("Periodic", card.TextContent, StringComparison.Ordinal);
            Assert.Contains("Clinic A", card.TextContent, StringComparison.Ordinal);
            Assert.Contains(
                "Fit with restrictions",
                page.Find(".crew-medical-current-fitness .crew-profile-indicator").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void No_examinations_shows_not_assessed_and_the_empty_state()
    {
        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Not assessed",
                page.Find(".crew-medical-current-fitness .crew-profile-indicator").TextContent,
                StringComparison.Ordinal);
            Assert.Contains("No medical examinations on record", page.Find(".crew-medical-card").TextContent, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false, false, "Valid", "crew-profile-indicator-valid")]
    [InlineData(true, false, "Expired", "crew-profile-indicator-expired")]
    [InlineData(false, true, "Expiring soon", "crew-profile-indicator-warning")]
    public void The_examination_status_badge_reflects_expiry_state(bool isExpired, bool isExpiringSoon, string label, string cssClass)
    {
        respondToMedicalExaminations = _ => Json(
            HttpStatusCode.OK,
            MedicalExaminations([MedicalExamination(isExpired: isExpired, isExpiringSoon: isExpiringSoon)]));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() =>
        {
            var badges = page.Find(".crew-medical-card .crew-profile-row").QuerySelectorAll(".crew-profile-indicator");
            var statusBadge = badges[1];
            Assert.Equal(label, statusBadge.TextContent.Trim());
            Assert.Contains(cssClass, statusBadge.ClassList);
        });
    }

    [Fact]
    public void Without_the_manage_permission_the_examination_form_is_not_offered()
    {
        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-medical-card")));
        Assert.Empty(page.FindAll("#medical-exam-type"));
    }

    [Fact]
    public void With_the_manage_permission_the_examination_form_is_offered()
    {
        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#medical-exam-type")));
    }

    [Fact]
    public void Registering_an_examination_refreshes_the_medical_section()
    {
        var callCount = 0;
        respondToMedicalExaminations = _ =>
        {
            callCount++;
            return callCount == 1
                ? Json(HttpStatusCode.OK, MedicalExaminations())
                : Json(HttpStatusCode.OK, MedicalExaminations([MedicalExamination()], currentFitnessResult: "Fit"));
        };

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#medical-exam-type")));
        FillMedicalExaminationForm(page);
        Closest(page, "#medical-exam-type", "form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Examination registered.", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Periodic", page.Find(".crew-medical-card").TextContent, StringComparison.Ordinal);
        });
        Assert.True(callCount >= 2);
    }

    [Fact]
    public void Medical_examination_validation_problems_are_listed()
    {
        respondToRegisterMedicalExamination = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"fitnessResult":["Fitness result is not supported."]}}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#medical-exam-type")));
        FillMedicalExaminationForm(page);
        Closest(page, "#medical-exam-type", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Fitness result is not supported.",
            page.Find(".crew-medical-card .crew-profile-form-field-errors").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void An_api_failure_loading_medical_examinations_shows_retry()
    {
        respondToMedicalExaminations = _ => Json(HttpStatusCode.InternalServerError, """{"status":500}""");

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.Find(".crew-medical-card").QuerySelectorAll(".api-error-state")));

        respondToMedicalExaminations = _ => Json(HttpStatusCode.OK, MedicalExaminations([MedicalExamination()]));
        page.Find(".crew-medical-card .api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("Periodic", page.Find(".crew-medical-card").TextContent, StringComparison.Ordinal));
    }

    // ---- Certificate details popup ----

    [Fact]
    public void Clicking_a_certificate_row_opens_its_details_and_closing_dismisses_it()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [Certificate()], assignments: []));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        var modal = page.Find(".crew-assignment-modal");
        Assert.Contains("Basic Safety Training", modal.TextContent, StringComparison.Ordinal);
        Assert.Contains("LIC-1001", modal.TextContent, StringComparison.Ordinal);
        Assert.Contains("Valid", modal.TextContent, StringComparison.Ordinal);

        page.Find(".crew-assignment-modal-close").Click();

        Assert.Empty(page.FindAll(".crew-assignment-modal"));
    }

    [Fact]
    public void Clicking_the_backdrop_also_closes_the_certificate_popup()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [Certificate()], assignments: []));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-assignment-modal")));

        page.Find(".crew-assignment-modal-backdrop").Click();

        Assert.Empty(page.FindAll(".crew-assignment-modal"));
    }

    // ---- Assign to vessel ----

    [Fact]
    public async Task Assigning_to_a_vessel_sends_scalar_ids_and_refreshes_only_the_assignments_section()
    {
        var callCount = 0;
        respondToProfile = _ =>
        {
            callCount++;
            return callCount == 1
                ? Json(HttpStatusCode.OK, Profile(
                    certificates: [Certificate(name: "Basic Safety")],
                    assignments: []))
                : Json(HttpStatusCode.OK, Profile(
                    certificates: [Certificate(name: "A Different Certificate")],
                    assignments: [Assignment(status: "Planned")]));
        };
        respondToRegisterAssignment = async request =>
        {
            writes.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync()));
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"id":"70000000-0000-0000-0000-000000000001"}""", Encoding.UTF8, "application/json"),
            };
        };

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.Contains("Basic Safety", page.Markup, StringComparison.Ordinal));

        await FillAssignmentFormAsync(page);
        Closest(page, "#assign-rank", "form").Submit();

        page.WaitForAssertion(() =>
        {
            var write = Assert.Single(writes);
            Assert.Equal("/api/crew/assignments", write.Path);
            Assert.Contains(VesselId.ToString(), write.Body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(RankId.ToString(), write.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Northern Star", write.Body, StringComparison.Ordinal);
        });

        page.WaitForAssertion(() => Assert.Contains("Crew member assigned to vessel.", page.Markup, StringComparison.Ordinal));

        // Only the assignments collection from the follow-up fetch should be applied.
        Assert.Contains("Basic Safety", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("A Different Certificate", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_open_assignment_conflict_is_shown_on_the_form()
    {
        respondToRegisterAssignment = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Crew member already has an open assignment.","status":409,"detail":"Crew member already has an open assignment."}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));
        await FillAssignmentFormAsync(page);
        Closest(page, "#assign-rank", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Ivan Petrović already has an open assignment.",
            page.Find(".crew-profile-form-conflict").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Assignment_validation_problems_from_the_api_are_listed_on_the_form()
    {
        respondToRegisterAssignment = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"rankId":["Selected rank is no longer available."]}}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal));
        await FillAssignmentFormAsync(page);
        Closest(page, "#assign-rank", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Selected rank is no longer available.",
            page.Markup,
            StringComparison.Ordinal));
    }

    // ---- Assignment details popup ----

    [Fact]
    public void Clicking_an_assignment_row_opens_its_details()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        var modal = page.Find(".crew-assignment-modal");
        Assert.Contains("Bosun", modal.TextContent, StringComparison.Ordinal);
        Assert.Contains("Active", modal.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_not_qualified_result_shows_a_non_blocking_warning_with_missing_types()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Planned")]));
        respondToQualificationEligibility = _ => Json(HttpStatusCode.OK, """
            {"crewMemberId":"10000000-0000-0000-0000-000000000001","rankId":"50000000-0000-0000-0000-000000000001",
            "from":"2026-01-01","to":"2026-06-01","status":"NotQualified","missingTypes":["STCW-II/1"],
            "uncoveredDates":[{"requiredType":"STCW-II/1","startDate":"2026-01-01","endDate":"2026-06-01"}],
            "certificateLinks":[]}
            """);

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "STCW-II/1", page.Find(".crew-qualification-warning").TextContent, StringComparison.Ordinal));

        // Non-blocking: the assignment can still be saved/planned despite the warning.
        Assert.Empty(page.FindAll(".crew-assignment-modal-delete[disabled]"));
    }

    [Fact]
    public void A_not_configured_policy_shows_no_warning()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));
        respondToQualificationEligibility = _ => Json(HttpStatusCode.OK, """
            {"crewMemberId":"10000000-0000-0000-0000-000000000001","rankId":"50000000-0000-0000-0000-000000000001",
            "from":"2026-01-01","to":"2026-06-01","status":"NotConfigured","missingTypes":[],"uncoveredDates":[],"certificateLinks":[]}
            """);

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-assignment-modal")));

        Assert.Empty(page.FindAll(".crew-qualification-warning"));
    }

    [Fact]
    public void Closing_the_popup_removes_it_from_the_page()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment()]));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();
        page.Find(".crew-assignment-modal-close").Click();

        Assert.Empty(page.FindAll(".crew-assignment-modal"));
    }

    [Fact]
    public void Without_the_manage_permission_the_delete_button_is_not_offered()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Planned")]));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        Assert.Empty(page.FindAll(".crew-assignment-modal-delete"));
    }

    [Fact]
    public void An_active_assignment_offers_lifecycle_actions()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        Assert.NotEmpty(page.FindAll(".crew-assignment-modal-delete"));
        Assert.Contains("Complete assignment", page.Find(".crew-assignment-modal").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelling_a_planned_assignment_closes_the_popup_and_refreshes_the_list()
    {
        var callCount = 0;
        respondToProfile = _ =>
        {
            callCount++;
            return callCount == 1
                ? Json(HttpStatusCode.OK, Profile(certificates: [], assignments: [Assignment(status: "Planned")]))
                : Json(HttpStatusCode.OK, Profile(certificates: [], assignments: []));
        };

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();
        page.Find(".crew-assignment-modal-delete").Click();
        page.FindAll(".crew-assignment-confirmation button").First(button => button.TextContent == "Confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".crew-assignment-modal"));
            Assert.Contains("No assignments on record", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_cancellation_conflict_is_shown_in_the_popup()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Planned")]));
        respondToDeleteAssignment = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Crew assignment cannot be cancelled.","status":409,"detail":"Crew assignment cannot be cancelled because it is already completed."}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();
        page.Find(".crew-assignment-modal-delete").Click();
        page.FindAll(".crew-assignment-confirmation button").First(button => button.TextContent == "Confirm").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Crew assignment cannot be cancelled because it is already completed.",
            page.Find(".crew-assignment-modal-error").TextContent,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".crew-assignment-modal"));
    }


    [Fact]
    public void An_open_assignment_with_no_plan_offers_the_plan_form_when_manage_is_granted()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Plan rotation",
                page.Find(".crew-assignment-modal .crew-profile-form-submit").TextContent,
                StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".crew-rotation-execute"));
        });
    }

    [Fact]
    public void The_plan_form_defaults_the_relief_date_to_the_assignments_planned_sign_off_date()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.Equal(
            "2026-06-01",
            page.Find("#rotation-planned-relief-date").GetAttribute("value")));
    }

    [Fact]
    public void Without_the_manage_permission_no_rotation_form_is_offered()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));

        var page = RenderProfilePage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-assignment-modal")));
        Assert.Empty(page.FindAll(".crew-assignment-modal .crew-profile-form-submit"));
    }

    [Fact]
    public void A_closed_assignment_offers_no_rotation_section()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active", actualSignOffDate: "2026-06-01")]));

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-assignment-modal")));
        Assert.Empty(page.FindAll(".crew-assignment-modal .crew-profile-form-submit"));
        Assert.DoesNotContain("Relief planning", page.Find(".crew-assignment-modal").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_existing_plan_shows_its_summary_and_the_update_form()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));
        respondToGetRotation = _ => Json(HttpStatusCode.OK, Rotation());

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() =>
        {
            var summary = page.Find(".crew-rotation-summary");
            Assert.Contains("Relief Member", summary.TextContent, StringComparison.Ordinal);
            Assert.Contains("4 months on / 2 months off", summary.TextContent, StringComparison.Ordinal);
            Assert.NotEmpty(page.FindAll(".crew-rotation-execute"));
        });

        Assert.Empty(page.FindAll("#rotation-pattern"));

        page.Find(".crew-rotation-edit-toggle").Click();

        Assert.Equal("4 months on / 2 months off", page.Find("#rotation-pattern").GetAttribute("value"));
        Assert.Contains(
            "Update rotation",
            page.Find(".crew-assignment-modal .crew-profile-form-submit").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Canceling_the_edit_hides_the_form_again_without_saving()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));
        respondToGetRotation = _ => Json(HttpStatusCode.OK, Rotation());

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-rotation-edit-toggle")));
        page.Find(".crew-rotation-edit-toggle").Click();

        Assert.NotEmpty(page.FindAll("#rotation-pattern"));
        page.Find("#rotation-pattern").Change("2 months on / 2 months off");

        page.Find(".crew-rotation-edit-toggle").Click();

        Assert.Empty(page.FindAll("#rotation-pattern"));
        Assert.Contains("4 months on / 2 months off", page.Find(".crew-rotation-summary").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_overdue_plan_shows_the_overdue_indicator()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));
        respondToGetRotation = _ => Json(HttpStatusCode.OK, Rotation(isOverdue: true));

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "Overdue",
            page.Find(".crew-assignment-modal .crew-profile-indicator-expired").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Planning_a_rotation_selects_a_relieving_member_and_shows_success()
    {
        var callCount = 0;
        respondToGetRotation = _ =>
        {
            callCount++;
            return callCount == 1
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
                : Json(HttpStatusCode.OK, Rotation());
        };
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".relieving-member-picker-input")));
        page.Find(".relieving-member-picker-input").Input("Relief");
        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".relieving-member-picker-option")),
            TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".relieving-member-picker-option").Click());

        page.Find("#rotation-pattern").Change("2 months on / 2 months off");
        page.Find("#rotation-planned-relief-date").Change("2026-07-01");
        Closest(page, "#rotation-pattern", "form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Rotation planned.", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_rotation_plan_shows_a_conflict_banner()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));
        respondToPlanRotation = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Crew assignment already has a planned rotation.","status":409,"detail":"Crew assignment already has a planned rotation."}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#rotation-pattern")));
        page.Find("#rotation-pattern").Change("2 months on / 2 months off");
        page.Find("#rotation-planned-relief-date").Change("2026-07-01");
        Closest(page, "#rotation-pattern", "form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Crew assignment already has a planned rotation.",
            page.Find(".crew-profile-form-conflict").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Executing_a_relief_closes_the_popup_and_refreshes_the_assignments_list()
    {
        var callCount = 0;
        respondToProfile = _ =>
        {
            callCount++;
            return callCount == 1
                ? Json(HttpStatusCode.OK, Profile(certificates: [], assignments: [Assignment(status: "Active")]))
                : Json(HttpStatusCode.OK, Profile(
                    certificates: [],
                    assignments: [Assignment(assignmentId: Guid.Parse("33000000-0000-0000-0000-000000000001"), status: "Active")]));
        };
        respondToGetRotation = _ => Json(HttpStatusCode.OK, Rotation());

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-rotation-execute")));
        page.Find(".crew-rotation-execute").Click();
        page.FindAll(".crew-assignment-confirmation button").First(button => button.TextContent == "Confirm").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".crew-assignment-modal")));
    }

    [Fact]
    public void An_execution_conflict_is_shown_and_the_popup_stays_open()
    {
        respondToProfile = _ => Json(
            HttpStatusCode.OK,
            Profile(certificates: [], assignments: [Assignment(status: "Active")]));
        respondToGetRotation = _ => Json(HttpStatusCode.OK, Rotation());
        respondToExecuteRotation = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Crew rotation cannot be executed.","status":409,"detail":"No relieving crew member is selected."}""");

        var page = RenderProfilePage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-profile-row-clickable")));
        page.Find(".crew-profile-row-clickable").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-rotation-execute")));
        page.Find(".crew-rotation-execute").Click();
        page.FindAll(".crew-assignment-confirmation button").First(button => button.TextContent == "Confirm").Click();

        page.WaitForAssertion(() => Assert.Contains(
            "No relieving crew member is selected.",
            page.Find(".crew-assignment-modal-error").TextContent,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".crew-assignment-modal"));
    }

    // ---- Helpers ----

    private static void FillCertificateForm(IRenderedComponent<CrewMemberProfile> page)
    {
        page.Find("#cert-type").Change("STCW");
        page.Find("#cert-name").Change("Basic Safety Training");
        page.Find("#cert-authority").Change("Flag State Authority");
        page.Find("#cert-license").Change("LIC-9001");
        page.Find("#cert-issue-date").Change("2026-01-01");
        page.Find("#cert-expiry-date").Change("2027-01-01");
    }

    private static void FillMedicalExaminationForm(IRenderedComponent<CrewMemberProfile> page)
    {
        page.Find("#medical-exam-type").Change("Periodic");
        page.Find("#medical-clinic").Change("Clinic A");
        page.Find("#medical-exam-date").Change("2026-01-01");
        page.Find("#medical-expiry-date").Change("2027-01-01");
        page.Find("#medical-fitness-result").Change("Fit");
    }

    private async Task FillAssignmentFormAsync(IRenderedComponent<CrewMemberProfile> page)
    {
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#assign-rank option[value='" + RankId + "']")));

        page.Find(".vessel-picker-input").Input("Northern");
        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".vessel-picker-option")),
            TimeSpan.FromSeconds(3));

        // Find and Click must be dispatched as one action: the debounced search can
        // trigger another render between finding and clicking the option, leaving a
        // stale event handler if they're issued separately.
        await page.InvokeAsync(() => page.Find(".vessel-picker-option").Click());

        page.Find("#assign-rank").Change(RankId.ToString());
        page.Find("#assign-sign-on-date").Change("2027-01-01");
        page.Find("#assign-planned-sign-off-date").Change("2027-06-01");
    }

    private static AngleSharp.Dom.IElement Closest(
        IRenderedComponent<CrewMemberProfile> page, string selector, string ancestorSelector)
    {
        return page.Find(selector).Closest(ancestorSelector)
            ?? throw new InvalidOperationException($"No ancestor '{ancestorSelector}' found for '{selector}'.");
    }

    private IRenderedComponent<CrewMemberProfile> RenderProfilePage(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/profile", StringComparison.Ordinal))
            {
                return respondToProfile(request);
            }

            if (path.EndsWith("/employment-status-history", StringComparison.Ordinal))
            {
                return respondToEmploymentStatusHistory(request);
            }

            if (path.Contains("/availability-requests/", StringComparison.Ordinal)
                && (path.EndsWith("/approve", StringComparison.Ordinal)
                    || path.EndsWith("/reject", StringComparison.Ordinal)
                    || path.EndsWith("/cancel", StringComparison.Ordinal)))
            {
                return respondToAvailabilityDecision(request);
            }

            if (path.EndsWith("/availability-requests", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return respondToAvailabilityRequest(request);
            }

            if (path.EndsWith("/availability/conflicts", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                return respondToAvailabilityConflicts(request);
            }

            if (path.EndsWith("/availability", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                return respondToMemberAvailability(request);
            }

            if (path.EndsWith("/employment-status-changes", StringComparison.Ordinal))
            {
                return respondToChangeEmploymentStatus(request);
            }

            if (path.EndsWith($"/members/{MemberId}", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
            {
                return respondToUpdateProfile(request);
            }

            if (path.EndsWith("/positions", StringComparison.Ordinal))
            {
                return respondToPositions(request);
            }

            if (path.EndsWith("/certificates", StringComparison.Ordinal))
            {
                return respondToRegisterCertificate(request);
            }

            if (path.EndsWith("/members", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                return respondToMemberSearch(request);
            }

            if (path.EndsWith("/assignments", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return respondToRegisterAssignment(request);
            }

            if (path.EndsWith("/cancel", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return respondToDeleteAssignment(request);
            }

            if (path.EndsWith("/rotation/execute", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return respondToExecuteRotation(request);
            }

            if (path.EndsWith("/rotation", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get)
                {
                    return respondToGetRotation(request);
                }

                if (request.Method == HttpMethod.Post)
                {
                    return respondToPlanRotation(request);
                }

                if (request.Method == HttpMethod.Put)
                {
                    return respondToUpdateRotation(request);
                }
            }

            if (path.EndsWith("/medical-examinations", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get)
                {
                    return respondToMedicalExaminations(request);
                }

                if (request.Method == HttpMethod.Post)
                {
                    return respondToRegisterMedicalExamination(request);
                }
            }

            if (path == "/api/crew/qualification-eligibility")
            {
                return respondToQualificationEligibility(request);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(request => respondToVesselSearch(request)))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CrewApiClient(httpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    permissions
                        .Select(permission => new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        return Render<CrewMemberProfile>(parameters => parameters.Add(p => p.CrewMemberId, MemberId));
    }

    private static string Profile(
        Guid? crewMemberId = null,
        string[]? certificates = null,
        string[]? assignments = null,
        string firstName = "Ivan",
        string employmentStatus = "Active")
    {
        var certificateJson = certificates ?? [Certificate()];
        var assignmentJson = assignments ?? [Assignment()];

        return $$"""
            {"crewMemberId":"{{crewMemberId ?? MemberId}}","firstName":"{{firstName}}","lastName":"Petrović","nationality":"Montenegro",
            "imoSeafarerId":"IMO100001","passportNumber":"P1234567","dateOfBirth":"1990-05-14",
            "passportExpiry":"2028-01-01","cdcNumber":"CDC987654","hireDate":"2024-01-15",
            "employmentStatus":"{{employmentStatus}}","baseRankId":"{{RankId}}","baseRankName":"Bosun",
            "certificates":[{{string.Join(',', certificateJson)}}],
            "assignments":[{{string.Join(',', assignmentJson)}}]}
            """;
    }

    private static string Certificate(
        Guid? certificateId = null,
        string name = "Basic Safety Training",
        DateOnly? expiryDate = null)
    {
        var expiry = expiryDate ?? DateOnly.FromDateTime(DateTime.Today).AddYears(1);

        return $$"""
            {"certificateId":"{{certificateId ?? Guid.Parse("30000000-0000-0000-0000-000000000001")}}",
            "type":"STCW","name":"{{name}}","issuingAuthority":"Flag State Authority",
            "licenseNumber":"LIC-1001","issueDate":"2024-01-01","expiryDate":"{{expiry:yyyy-MM-dd}}",
            "status":"Valid"}
            """;
    }

    private static string Assignment(
        Guid? assignmentId = null,
        string status = "Active",
        string? actualSignOffDate = null)
    {
        var actual = actualSignOffDate is null ? "null" : $"\"{actualSignOffDate}\"";

        return $$"""
            {"assignmentId":"{{assignmentId ?? Guid.Parse("31000000-0000-0000-0000-000000000001")}}",
            "vesselId":"{{VesselId}}","rankId":"{{RankId}}","rankName":"Bosun",
            "signOnPortId":null,"signOffPortId":null,"signOnDate":"2026-01-01",
            "plannedSignOffDate":"2026-06-01","actualSignOffDate":{{actual}},"status":"{{status}}"}
            """;
    }

    private static string MedicalExaminations(string[]? items = null, string? currentFitnessResult = null) => $$"""
        {"items":[{{string.Join(',', items ?? [])}}],
        "currentFitnessResult":{{(currentFitnessResult is null ? "null" : $"\"{currentFitnessResult}\"")}}}
        """;

    private static string MedicalExamination(
        Guid? examinationId = null,
        string examType = "Periodic",
        string clinic = "Clinic A",
        string fitnessResult = "Fit",
        bool isExpired = false,
        bool isExpiringSoon = false) => $$"""
        {"medicalExaminationId":"{{examinationId ?? Guid.Parse("91000000-0000-0000-0000-000000000001")}}",
        "crewMemberId":"{{MemberId}}","crewMemberName":"Ivan Petrović","employmentStatus":"Active",
        "examType":"{{examType}}","clinic":"{{clinic}}","examDate":"2026-01-01","expiryDate":"2027-01-01",
        "fitnessResult":"{{fitnessResult}}","isExpired":{{(isExpired ? "true" : "false")}},
        "isExpiringSoon":{{(isExpiringSoon ? "true" : "false")}}}
        """;

    private static string Rotation(bool isOverdue = false) => $$"""
        {"rotationId":"81000000-0000-0000-0000-000000000001","assignmentId":"31000000-0000-0000-0000-000000000001",
        "vesselId":"{{VesselId}}","crewMemberId":"{{MemberId}}","crewMemberName":"Ivan Petrović",
        "rankId":"{{RankId}}","rankName":"Bosun","relievingCrewMemberId":"20000000-0000-0000-0000-000000000002",
        "relievingCrewMemberName":"Relief Member","rotationPattern":"4 months on / 2 months off","plannedReliefDate":"2026-07-01",
        "isOverdue":{{(isOverdue ? "true" : "false")}}}
        """;

    private static string Positions(params string[] rows) =>
        $$"""{"items":[{{string.Join(',', rows)}}]}""";

    private static string Position(Guid? crewPositionId = null) => $$"""
        {"crewPositionId":"{{crewPositionId ?? RankId}}","rankName":"Bosun","department":"Deck","seniorityLevel":"Senior"}
        """;

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
