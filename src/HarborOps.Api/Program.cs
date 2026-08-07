using HarborOps.Infrastructure;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Identity.Presentation;
using HarborOps.Modules.Maintenance;
using HarborOps.Modules.Voyages;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HarborOpsDatabase")
    ?? throw new InvalidOperationException("Connection string 'HarborOpsDatabase' was not found.");

builder.Services.AddHarborOpsInfrastructure(connectionString);
builder.Services.AddCargoModule();
builder.Services.AddDocumentsModule();
builder.Services.AddFleetModule();
builder.Services.AddComplianceModule();
builder.Services.AddCrewModule();
builder.Services.AddVoyagesModule();
builder.Services.AddMaintenanceModule();

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
app.MapHealthChecks("/health");

app.Run();
/// <summary>
/// Defines the <see cref="Program" />
/// </summary>
public partial class Program;
