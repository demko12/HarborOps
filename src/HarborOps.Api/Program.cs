using System.Threading.RateLimiting;
using HarborOps.Api.Composition.Authorization;
using HarborOps.Api.Composition.Documents;
using HarborOps.Api.Composition.Maintenance;
using HarborOps.Api.Composition.Notifications;
using HarborOps.Api.Composition.Operations.Dashboard;
using HarborOps.Api.Composition.Operations.FleetVessel;
using HarborOps.Api.Composition.Reports.ComplianceExposure;
using HarborOps.Api.Composition.Reports.DocumentsExpiry;
using HarborOps.Api.Composition.Reports.MaintenanceBacklog;
using HarborOps.Api.Composition.Voyages;
using HarborOps.Infrastructure;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Identity;
using HarborOps.Modules.Identity.Application;
using HarborOps.Modules.Identity.Presentation;
using HarborOps.Modules.Maintenance;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Notifications;
using HarborOps.Modules.Voyages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using CrewComposition = HarborOps.Api.Composition.Crew;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HarborOpsDatabase")
    ?? throw new InvalidOperationException("Connection string 'HarborOpsDatabase' was not found.");

builder.Services.AddHarborOpsInfrastructure(connectionString);
builder.Services.AddCargoModule();
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddScoped<HarborOps.Modules.Documents.Contracts.IDocumentDistributionRecipientLookup, IdentityDocumentDistributionRecipientLookup>();
builder.Services.AddScoped<HarborOps.Modules.Documents.Contracts.IVesselExistenceLookup, FleetDocumentVesselExistenceLookup>();
builder.Services.AddScoped<HarborOps.Modules.Documents.Contracts.IVesselDirectory, FleetDocumentVesselDirectory>();
builder.Services.AddFleetModule();
builder.Services.AddComplianceModule();
builder.Services.AddCrewModule(builder.Configuration);
builder.Services.AddScoped<HarborOps.Modules.Crew.Contracts.IVesselExistenceLookup, CrewComposition.FleetVesselExistenceLookup>();
builder.Services.AddVoyagesModule();
builder.Services.AddScoped<FleetVesselOverviewService>();
builder.Services.AddScoped<OperationsDashboardService>();
builder.Services.AddScoped<VoyageEstimateRevisionService>();
builder.Services.AddMaintenanceModule();
builder.Services.AddScoped<IVesselExistenceLookup, FleetVesselExistenceLookup>();
builder.Services.AddNotificationsModule();
builder.Services.AddScoped<DocumentsExpiryReportService>();
builder.Services.AddScoped<ComplianceExposureReportService>();
builder.Services.AddScoped<MaintenanceBacklogReportService>();
builder.Services.AddScoped<ExpiryAlertGenerator>();
builder.Services.Configure<ExpiryAlertOptions>(builder.Configuration.GetSection(ExpiryAlertOptions.SectionName));
builder.Services.AddHostedService<ExpiryAlertHostedService>();
builder.Services.Configure<LoginThrottlingOptions>(builder.Configuration.GetSection(LoginThrottlingOptions.SectionName));
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, cancellationToken) =>
    {
        var loginSettings = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<LoginThrottlingOptions>>().Value;
        await LoginThrottlingMiddleware.WriteThrottledResponseAsync(
            context.HttpContext,
            loginSettings.ClientAttemptWindow,
            cancellationToken);
    };
    options.AddPolicy(LoginThrottlingOptions.IpPolicyName, context =>
    {
        var loginSettings = context.RequestServices.GetRequiredService<IOptions<LoginThrottlingOptions>>().Value;
        return
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginSettings.ClientAttemptLimit,
                Window = loginSettings.ClientAttemptWindow,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});


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

app.UseRateLimiter();
app.UseMiddleware<LoginThrottlingMiddleware>();
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
