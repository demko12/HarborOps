using HarborOps.Infrastructure;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Identity;
using HarborOps.Modules.Voyages;
using HarborOps.Modules.Maintenance;

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


builder.Services.AddIdentityModule()
    .AddEntityFrameworkStores<HarborOpsDbContext>();

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
