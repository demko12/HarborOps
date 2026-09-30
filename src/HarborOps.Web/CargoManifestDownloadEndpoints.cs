using HarborOps.Web.Api.Cargo;

namespace HarborOps.Web;

internal static class CargoManifestDownloadEndpoints
{
    internal const string ManifestExportRoute = "/cargo/{bookingId:guid}/manifest/export";
    internal const string MatchingCountHeader = "X-Matching-Count";

    internal static IApplicationBuilder UseNoStoreForManifestPrint(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (IsManifestPrintPath(context.Request.Path))
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            await next(context);
        });
    }

    internal static bool IsManifestPrintPath(PathString path)
    {
        var segments = path.Value?.Trim('/').Split('/') ?? [];

        return segments.Length == 4
            && string.Equals(segments[0], "cargo", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(segments[1], out _)
            && string.Equals(segments[2], "manifest", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[3], "print", StringComparison.OrdinalIgnoreCase);
    }

    internal static IEndpointRouteBuilder MapCargoManifestDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cargo/container-manifest-template", DownloadAsync)
            .RequireAuthorization("AccessCargo");

        app.MapGet(ManifestExportRoute, DownloadManifestAsync)
            .RequireAuthorization("AccessCargo");

        return app;
    }

    private static async Task<IResult> DownloadAsync(
        string? format,
        CargoBookingsApiClient cargoApi,
        CancellationToken cancellationToken)
    {
        if (format is not ("xlsx" or "xml")) return Results.BadRequest("Format must be xlsx or xml.");

        using var response = await cargoApi.DownloadManifestTemplateAsync(format, cancellationToken);
        if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"container-manifest-template.{format}";

        return Results.File(bytes, contentType, fileName);
    }

    internal static async Task<IResult> DownloadManifestAsync(
        Guid bookingId,
        string? search,
        string? status,
        string? containerType,
        string? portOfLoadingCode,
        string? portOfDischargeCode,
        HttpContext context,
        CargoBookingsApiClient cargoApi,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        var filter = CargoContainerFilter.Create(search, status, containerType, portOfLoadingCode, portOfDischargeCode);

        try
        {
            using var response = await cargoApi.DownloadManifestCsvAsync(bookingId, filter, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                return Results.Text(body, contentType, statusCode: (int)response.StatusCode);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "manifest.csv";

            if (response.Headers.TryGetValues(MatchingCountHeader, out var matchingCount))
            {
                context.Response.Headers[MatchingCountHeader] = matchingCount.ToArray();
            }

            return Results.File(bytes, contentType, fileName);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                title: "HarborOps API is unavailable",
                detail: "The manifest could not be exported. Please try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
