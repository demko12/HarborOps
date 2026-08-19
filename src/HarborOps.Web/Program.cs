using HarborOps.Web;
using HarborOps.Web.Api;
using HarborOps.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ThemePreferenceState>();
builder.Services.AddHarborOpsApi(builder.Configuration);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = "HarborOps.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<ServerSessionTicketStore>((options, ticketStore) => options.SessionStore = ticketStore);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManageAdministration", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.ManageUsersPermission)
        .RequireClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.ManageRolesPermission));
    options.AddPolicy("AccessFleet", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.FleetReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.FleetManagePermission)));
    options.AddPolicy("AccessCrew", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.CrewReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.CrewManagePermission)));
    options.AddPolicy("AccessVoyages", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.VoyagesReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.VoyagesManagePermission)));
    options.AddPolicy("AccessCargo", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.CargoReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.CargoManagePermission)));
    options.AddPolicy("AccessMaintenance", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.MaintenanceReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.MaintenanceManagePermission)));
    options.AddPolicy("AccessDocuments", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.DocumentsReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.DocumentsManagePermission)));
    options.AddPolicy("AccessCompliance", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.ComplianceReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.ComplianceManagePermission)));
    options.AddPolicy("AccessReports", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.ReportsReadPermission)
        || context.User.HasClaim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.ReportsManagePermission)));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.MapWebAuthenticationEndpoints();
app.MapDocumentFileDownloadEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
