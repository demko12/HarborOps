using HarborOps.Web.Api.Documents;

namespace HarborOps.Web;

internal static class DocumentFileDownloadEndpoints
{
    internal static IEndpointRouteBuilder MapDocumentFileDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/documents/{documentId:guid}/file/download", DownloadAsync)
            .RequireAuthorization("AccessDocuments");

        return app;
    }

    private static async Task<IResult> DownloadAsync(
        Guid documentId,
        DocumentsApiClient documentsApi,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await documentsApi.DownloadDocumentFileAsync(documentId, cancellationToken);

        if (!result.IsSuccess)
        {
            var failure = result.Failure!;

            return failure.IsNotFound
                ? Results.NotFound()
                : Results.StatusCode(failure.StatusCode == 0 ? StatusCodes.Status502BadGateway : failure.StatusCode);
        }

        using var response = result.Value!;

        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        context.Response.ContentLength = response.Content.Headers.ContentLength;

        if (response.Content.Headers.ContentDisposition is { } contentDisposition)
        {
            context.Response.Headers.ContentDisposition = contentDisposition.ToString();
        }

        if (response.Headers.TryGetValues("X-Content-Type-Options", out var nosniffValues))
        {
            context.Response.Headers["X-Content-Type-Options"] = nosniffValues.ToArray();
        }

        await using var backendStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await backendStream.CopyToAsync(context.Response.Body, cancellationToken);

        return Results.Empty;
    }
}
