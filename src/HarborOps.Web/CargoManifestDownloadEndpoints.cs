using HarborOps.Web.Api.Cargo;

namespace HarborOps.Web;

internal static class CargoManifestDownloadEndpoints
{
    internal static IEndpointRouteBuilder MapCargoManifestDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cargo/container-manifest-template", DownloadAsync)
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
}
