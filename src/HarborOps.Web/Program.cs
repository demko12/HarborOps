using HarborOps.Web;
using HarborOps.Web.Api;
using HarborOps.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
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
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
