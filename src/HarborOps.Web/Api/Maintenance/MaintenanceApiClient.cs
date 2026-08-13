using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace HarborOps.Web.Api.Maintenance;

internal sealed class MaintenanceApiClient(
    HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    internal async Task<GetWorkOrdersResult> GetWorkOrdersAsync(
        MaintenanceWorkOrderFilter filter,
        CancellationToken cancellationToken)
    {
        var queryParameters = new Dictionary<string, string?>
        {
            ["pageNumber"] = filter.PageNumber.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = filter.PageSize.ToString(CultureInfo.InvariantCulture),
        };

        if (filter.VesselId is { } vesselId)
        {
            queryParameters["vesselId"] = vesselId.ToString();
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            queryParameters["status"] = filter.Status;
        }

        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            queryParameters["priority"] = filter.Priority;
        }

        if (filter.Overdue is { } overdue)
        {
            queryParameters["overdue"] = overdue ? "true" : "false";
        }

        var uri = QueryHelpers.AddQueryString("api/maintenance/work-orders", queryParameters);

        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var page = await response.Content.ReadFromJsonAsync<MaintenanceWorkOrdersPage>(
                    JsonOptions, cancellationToken);
                return new GetWorkOrdersResult(MaintenanceRequestStatus.Success, page, null, null);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                return new GetWorkOrdersResult(
                    MaintenanceRequestStatus.ValidationFailed, null, problem?.Errors, problem?.Title);
            }

            return new GetWorkOrdersResult(
                MaintenanceRequestStatus.Error, null, null, "Unable to load work orders right now.");
        }
        catch (HttpRequestException)
        {
            return new GetWorkOrdersResult(
                MaintenanceRequestStatus.Error, null, null, "The maintenance service could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GetWorkOrdersResult(
                MaintenanceRequestStatus.Error, null, null, "The request timed out.");
        }
    }

    internal async Task<CreateWorkOrderResult> CreateWorkOrderAsync(
        CreateMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/maintenance/work-orders", request, JsonOptions, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var created = await response.Content.ReadFromJsonAsync<CreatedWorkOrder>(
                    JsonOptions, cancellationToken);
                return new CreateWorkOrderResult(MaintenanceRequestStatus.Success, created?.Id, null, null);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                return new CreateWorkOrderResult(
                    MaintenanceRequestStatus.ValidationFailed, null, problem?.Errors, problem?.Title);
            }

            return new CreateWorkOrderResult(
                MaintenanceRequestStatus.Error, null, null, "Unable to create the work order right now.");
        }
        catch (HttpRequestException)
        {
            return new CreateWorkOrderResult(
                MaintenanceRequestStatus.Error, null, null, "The maintenance service could not be reached.");
        }
    }

    internal async Task<CompleteWorkOrderResult> CompleteWorkOrderAsync(
        Guid workOrderId,
        CompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"api/maintenance/work-orders/{workOrderId}/completion", request, JsonOptions, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var result = await response.Content.ReadFromJsonAsync<CompleteMaintenanceWorkOrderApiResult>(
                    JsonOptions, cancellationToken);
                return new CompleteWorkOrderResult(
                    MaintenanceRequestStatus.Success, result?.CompletedUtc, null, null);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                return new CompleteWorkOrderResult(
                    MaintenanceRequestStatus.ValidationFailed, null, problem?.Errors, problem?.Title);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                return new CompleteWorkOrderResult(
                    MaintenanceRequestStatus.Conflict,
                    null,
                    null,
                    problem?.Detail ?? "This work order can no longer be completed.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new CompleteWorkOrderResult(
                    MaintenanceRequestStatus.NotFound, null, null, "This work order was not found.");
            }

            return new CompleteWorkOrderResult(
                MaintenanceRequestStatus.Error, null, null, "Unable to complete the work order right now.");
        }
        catch (HttpRequestException)
        {
            return new CompleteWorkOrderResult(
                MaintenanceRequestStatus.Error, null, null, "The maintenance service could not be reached.");
        }
    }

    private static async Task<ApiProblemDetails?> ReadProblemAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblemDetails>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CreatedWorkOrder(Guid Id);
}
