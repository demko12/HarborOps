using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Features.Maintenance;

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

        services.AddHttpClient<MaintenanceApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<FleetApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<IdentityAdministrationApiClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<BearerTokenHandler>();
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton<ServerSessionTicketStore>();
        services.AddTransient<BearerTokenHandler>();
        services.AddScoped<CurrentUserAuthorizationState>();
        services.AddScoped<MaintenanceWorkOrderBoard>();

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
