using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Api.Voyages;

using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Api;

internal static class ApiConfiguration
{
    internal const string BaseUrlKey = "ApiBaseUrl";

    internal static IServiceCollection AddHarborOpsApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseAddress = GetBaseAddress(configuration);

        services.AddHttpClient<HealthApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddHttpClient<IdentitySessionApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient<CargoBookingsApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<BearerTokenHandler>();

        services.AddHttpClient<MaintenanceApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddHttpMessageHandler<BearerTokenHandler>();

        services.AddHttpClient<VesselLookupApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddHttpMessageHandler<BearerTokenHandler>();

        services.AddHttpClient<IdentityAdministrationApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<BearerTokenHandler>();

        services.AddHttpClient<FleetApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<BearerTokenHandler>();
        services.AddHttpClient<CrewApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<BearerTokenHandler>();

        services.AddHttpClient<VoyagesApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<BearerTokenHandler>();

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton<ServerSessionTicketStore>();
        services.AddTransient<BearerTokenHandler>();
        services.AddScoped<CurrentUserAuthorizationState>();

        return services;
    }

    internal static Uri GetBaseAddress(IConfiguration configuration)
    {
        var configuredBaseUrl = configuration[BaseUrlKey];
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            throw new InvalidOperationException(
                $"Configuration value '{BaseUrlKey}' is required.");
        }

        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseAddress)
            || (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                && !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Configuration value '{BaseUrlKey}' must be an absolute HTTP or HTTPS URL.");
        }

        return new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + '/', UriKind.Absolute);
    }
}
