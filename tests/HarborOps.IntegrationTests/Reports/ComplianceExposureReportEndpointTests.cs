using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Reports.ComplianceExposure;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Reports;

public sealed class ComplianceExposureReportEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public ComplianceExposureReportEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_composed_page_mixing_categories()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        await SeedCertificateAsync(vesselId, "CERT-1", asOf.AddDays(10));
        await SeedInsurancePolicyAsync(vesselId, "POL-1", asOf.AddDays(20));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/compliance-exposure?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<ComplianceExposureReportPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(asOf, page.AsOf);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["Certificate", "Insurance"], page.Items.Select(item => item.ItemType));
    }

    [Fact]
    public async Task Get_filters_by_category()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        await SeedCertificateAsync(vesselId, "CERT-1", asOf.AddDays(10));
        await SeedInsurancePolicyAsync(vesselId, "POL-1", asOf.AddDays(20));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/compliance-exposure?asOf={asOf:yyyy-MM-dd}&category=Insurance",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<ComplianceExposureReportPage>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal("Insurance", item.ItemType);
        Assert.Equal("POL-1", item.Reference);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_an_unrecognized_category()
    {
        var asOf = new DateOnly(2026, 8, 15);
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync(
            $"/api/reports/compliance-exposure?asOf={asOf:yyyy-MM-dd}&category=Bogus",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ComplianceExposureReportValidator.CategoryField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_when_as_of_is_missing()
    {
        using var client = await CreateReportClientAsync();

        using var response = await client.GetAsync("/api/reports/compliance-exposure", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ComplianceExposureReportValidator.AsOfField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_unauthorized_for_an_anonymous_request()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            "/api/reports/compliance-exposure?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_reports_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Compliance.Read);

        using var response = await client.GetAsync(
            "/api/reports/compliance-exposure?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_compliance_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Reports.Read);

        using var response = await client.GetAsync(
            "/api/reports/compliance-exposure?asOf=2026-08-15",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_returns_csv_with_headers_matching_json_rows()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        await SeedCertificateAsync(vesselId, "CERT-1", asOf.AddDays(10));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/compliance-exposure/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(
            $"compliance-exposure-{asOf:yyyy-MM-dd}.csv",
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var lines = csv.Replace("﻿", string.Empty).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("ItemType,VesselId,Reference,Description,RelevantDate,Status,Severity", lines[0]);
        Assert.Contains("CERT-1", lines[1]);
    }

    [Fact]
    public async Task Export_leaves_severity_empty_for_categories_without_a_severity()
    {
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 15);
        await SeedCertificateAsync(vesselId, "CERT-1", asOf.AddDays(10));

        using var client = await CreateReportClientAsync();
        using var response = await client.GetAsync(
            $"/api/reports/compliance-exposure/export?asOf={asOf:yyyy-MM-dd}",
            CancellationToken.None);
        var csv = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.EndsWith(",Expiring,", csv.TrimEnd('\r', '\n'));
    }

    private Task<HttpClient> CreateReportClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Reports.Read,
            Permissions.Compliance.Read);
    }

    private async Task SeedCertificateAsync(Guid vesselId, string certificateNumber, DateOnly expiresOn)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var certificate = ComplianceCertificate.Register(
            vesselId, "SMC", certificateNumber, "Flag State Authority", expiresOn.AddYears(-1), expiresOn);
        await dbContext.AddAsync(certificate);
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedInsurancePolicyAsync(Guid vesselId, string policyNumber, DateOnly expiryDate)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var policy = new InsurancePolicy(vesselId, "Insurer", policyNumber, "Hull", expiryDate);
        await dbContext.AddAsync(policy);
        await dbContext.SaveChangesAsync();
    }
}
