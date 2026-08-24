using HarborOps.Api.Composition.Authorization;
using HarborOps.Api.Composition.Operations.Dashboard;
using HarborOps.Api.Composition.Operations.FleetVessel;
using HarborOps.Api.Composition.Reports.ComplianceExposure;
using HarborOps.Api.Composition.Reports.DocumentsExpiry;
using HarborOps.Api.Composition.Reports.MaintenanceBacklog;
using HarborOps.Infrastructure;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Identity;
using HarborOps.Modules.Identity.Presentation;
using HarborOps.Modules.Maintenance;
using HarborOps.Modules.Voyages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HarborOpsDatabase")
    ?? throw new InvalidOperationException("Connection string 'HarborOpsDatabase' was not found.");

builder.Services.AddHarborOpsInfrastructure(connectionString);
builder.Services.AddCargoModule();
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddFleetModule();
builder.Services.AddComplianceModule();
builder.Services.AddCrewModule();
builder.Services.AddVoyagesModule();
builder.Services.AddScoped<FleetVesselOverviewService>();
builder.Services.AddScoped<OperationsDashboardService>();
builder.Services.AddMaintenanceModule();
builder.Services.AddScoped<DocumentsExpiryReportService>();
builder.Services.AddScoped<ComplianceExposureReportService>();
builder.Services.AddScoped<MaintenanceBacklogReportService>();


builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

builder.Services.Configure<MvcOptions>(options =>
    options.Conventions.Add(new CapabilityAuthorizationConvention()));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        IdentityEndpoints.UnavailableEndpointPolicy,
        policy => policy.RequireAssertion(_ => true));

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "HarborOps API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapIdentityModuleEndpoints();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

/// <summary>
/// Defines the <see cref="Program" />
/// </summary>
public partial class Program;
