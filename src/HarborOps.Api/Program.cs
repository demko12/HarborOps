using HarborOps.Infrastructure;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Voyages;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HarborOpsDatabase")
    ?? throw new InvalidOperationException("Connection string 'HarborOpsDatabase' was not found.");

builder.Services.AddHarborOpsInfrastructure(connectionString);
builder.Services.AddFleetModule();
builder.Services.AddCrewModule();
builder.Services.AddVoyagesModule();
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
