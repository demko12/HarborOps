using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.Web.Api.Cargo;

internal sealed class CargoBookingsApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/cargo/bookings";
    private const string BillsPath = "api/cargo/bills-of-lading";
    private const string ContainersPath = "api/cargo/containers";
    private const string OperationsPath = "api/cargo/operations";

    private const string ReviewAccessDetail = "Please review your access and try again.";
    private const string ReviewValuesDetail = "Please review the values and try again.";

    internal Task<CargoApiResult<CargoBookingPage>> SearchAsync(
        CargoBookingFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return GetAsync<CargoBookingPage>(
            BuildPath(filter),
            "Unable to load cargo bookings",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoBookingDetails>> GetDetailsAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<CargoBookingDetails>(
            $"{BasePath}/{bookingId}",
            "Unable to load the cargo booking",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<RegisteredCargoBooking>> RegisterAsync(
        RegisterCargoBookingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RegisteredCargoBooking>(
            BasePath,
            request,
            "Unable to register the cargo booking",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoSafetyInformationView>> GetSafetyInformationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<CargoSafetyInformationView>(
            SafetyPath(bookingId),
            "Unable to load the safety information",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoSafetyInformation>> SaveSafetyInformationAsync(
        Guid bookingId,
        UpdateCargoSafetyInfoWebRequest request,
        CancellationToken cancellationToken)
    {
        return PutAsync<CargoSafetyInformation>(
            SafetyPath(bookingId),
            request,
            "Unable to save the safety information",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoSafetyInformation>> SubmitSafetyInformationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoSafetyInformation>(
            $"{SafetyPath(bookingId)}/submit",
            "Unable to submit the safety information",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoSafetyInformation>> ReviewSafetyInformationAsync(
        Guid bookingId,
        ReviewCargoSafetyInfoWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoSafetyInformation>(
            $"{SafetyPath(bookingId)}/review",
            request,
            "Unable to review the safety information",
            cancellationToken);
    }

    internal Task<CargoApiResult<RecordedCargoTrackingEvent>> RecordTrackingEventAsync(
        Guid bookingId,
        RecordCargoTrackingEventWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RecordedCargoTrackingEvent>(
            $"{BasePath}/{bookingId}/tracking-events",
            request,
            "Unable to record the tracking event",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoContainerList>> GetContainersAsync(
        Guid bookingId,
        CancellationToken cancellationToken) =>
        GetContainersAsync(bookingId, 1, cancellationToken);

    internal Task<CargoApiResult<CargoContainerList>> GetContainersAsync(
        Guid bookingId,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        return GetAsync<CargoContainerList>(
            $"{BasePath}/{bookingId}/containers?pageNumber={pageNumber}&pageSize=15",
            "Unable to load the container manifest",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<AddedCargoContainer>> AddContainerAsync(
        Guid bookingId,
        AddCargoContainerWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<AddedCargoContainer>(
            $"{BasePath}/{bookingId}/containers",
            request,
            "Unable to add the container",
            cancellationToken);
    }

    internal Task<HttpResponseMessage> DownloadManifestTemplateAsync(
        string format,
        CancellationToken cancellationToken) =>
        httpClient.GetAsync($"api/cargo/container-manifest-template?format={Uri.EscapeDataString(format)}", cancellationToken);

    internal Task<CargoApiResult<UpdatedCargoContainer>> UpdateContainerAsync(
        Guid bookingId,
        Guid containerId,
        UpdateCargoContainerWebRequest request,
        CancellationToken cancellationToken)
    {
        return PutAsync<UpdatedCargoContainer>(
            $"{BasePath}/{bookingId}/containers/{containerId}",
            request,
            "Unable to update the container",
            cancellationToken);
    }

    internal Task<CargoApiResult<ContainerManifestPreview>> PreviewContainerManifestAsync(Guid bookingId, Stream stream, string fileName, CancellationToken cancellationToken)
        => SendManifestAsync<ContainerManifestPreview>($"{BasePath}/{bookingId}/containers/import/preview", stream, fileName, "Unable to preview the manifest", cancellationToken);

    internal Task<CargoApiResult<ImportedContainerManifest>> ImportContainerManifestAsync(Guid bookingId, Stream stream, string fileName, CancellationToken cancellationToken)
        => SendManifestAsync<ImportedContainerManifest>($"{BasePath}/{bookingId}/containers/import", stream, fileName, "Unable to import the manifest", cancellationToken);

    internal Task<CargoApiResult<CargoOperationList>> GetOperationsAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<CargoOperationList>(
            $"{BasePath}/{bookingId}/operations",
            "Unable to load the cargo operations",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<PlannedCargoOperation>> PlanOperationAsync(
        Guid bookingId,
        PlanCargoOperationWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<PlannedCargoOperation>(
            $"{BasePath}/{bookingId}/operations",
            request,
            "Unable to plan the operation",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoOperationTransition>> StartOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoOperationTransition>(
            $"{OperationsPath}/{operationId}/start",
            "Unable to start the operation",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoOperationTransition>> CompleteOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoOperationTransition>(
            $"{OperationsPath}/{operationId}/complete",
            "Unable to complete the operation",
            cancellationToken);
    }

    internal Task<CargoApiResult<ContainerControlView>> GetContainerControlAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        return GetAsync<ContainerControlView>(
            $"{ContainerPath(containerId)}/control",
            "Unable to load the container control",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<ContainerControlRow>> AssignContainerSealAsync(
        Guid containerId,
        AssignContainerSealWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ContainerControlRow>(
            $"{ContainerPath(containerId)}/seal",
            request,
            "Unable to assign the seal",
            cancellationToken);
    }

    internal Task<CargoApiResult<ContainerControlRow>> BreakContainerSealAsync(
        Guid containerId,
        BreakContainerSealWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ContainerControlRow>(
            $"{ContainerPath(containerId)}/seal/break",
            request,
            "Unable to break the seal",
            cancellationToken);
    }

    internal Task<CargoApiResult<ContainerControlRow>> RecordContainerInspectionAsync(
        Guid containerId,
        RecordContainerInspectionWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ContainerControlRow>(
            $"{ContainerPath(containerId)}/inspections",
            request,
            "Unable to record the inspection",
            cancellationToken);
    }

    internal Task<CargoApiResult<ContainerControlRow>> ReleaseContainerAsync(
        Guid containerId,
        ReleaseContainerWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ContainerControlRow>(
            $"{ContainerPath(containerId)}/release",
            request,
            "Unable to release the container",
            cancellationToken);
    }

    internal Task<CargoApiResult<ContainerControlRow>> HoldContainerAsync(
        Guid containerId,
        HoldContainerWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ContainerControlRow>(
            $"{ContainerPath(containerId)}/hold",
            request,
            "Unable to hold the container",
            cancellationToken);
    }

    internal Task<CargoApiResult<BillOfLadingList>> GetBillsOfLadingAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<BillOfLadingList>(
            BillsForBookingPath(bookingId),
            "Unable to load the bills of lading",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<BillOfLadingView>> GetBillOfLadingAsync(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<BillOfLadingView>(
            BillPath(billOfLadingId),
            "Unable to load the bill of lading",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<BillOfLadingPrintRow>> GetBillOfLadingPrintAsync(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<BillOfLadingPrintRow>(
            $"{BillPath(billOfLadingId)}/print",
            "Unable to load the printable bill of lading",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<BillOfLadingRow>> CreateBillOfLadingAsync(
        Guid bookingId,
        SaveBillOfLadingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<BillOfLadingRow>(
            BillsForBookingPath(bookingId),
            request,
            "Unable to create the bill of lading",
            cancellationToken);
    }

    internal Task<CargoApiResult<BillOfLadingRow>> UpdateBillOfLadingAsync(
        Guid billOfLadingId,
        SaveBillOfLadingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PutAsync<BillOfLadingRow>(
            BillPath(billOfLadingId),
            request,
            "Unable to save the bill of lading",
            cancellationToken);
    }

    internal Task<CargoApiResult<BillOfLadingRow>> IssueBillOfLadingAsync(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        return PostAsync<BillOfLadingRow>(
            $"{BillPath(billOfLadingId)}/issue",
            "Unable to issue the bill of lading",
            cancellationToken);
    }

    internal Task<CargoApiResult<BillOfLadingRow>> VoidBillOfLadingAsync(
        Guid billOfLadingId,
        VoidBillOfLadingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<BillOfLadingRow>(
            $"{BillPath(billOfLadingId)}/void",
            request,
            "Unable to void the bill of lading",
            cancellationToken);
    }

    private static string BillsForBookingPath(Guid bookingId)
    {
        return $"{BasePath}/{bookingId}/bills-of-lading";
    }

    private static string BillPath(Guid billOfLadingId)
    {
        return $"{BillsPath}/{billOfLadingId}";
    }

    private static string ContainerPath(Guid containerId)
    {
        return $"{ContainersPath}/{containerId}";
    }

    private static string SafetyPath(Guid bookingId)
    {
        return $"{BasePath}/{bookingId}/safety-information";
    }
    private static string BuildPath(CargoBookingFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber}",
            $"pageSize={filter.PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query.Insert(0, $"search={Uri.EscapeDataString(filter.Search.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Insert(0, $"status={Uri.EscapeDataString(filter.Status)}");
        }

        if (filter.IsHazardous is { } isHazardous)
        {
            query.Add($"isHazardous={(isHazardous ? "true" : "false")}");
        }

        return $"{BasePath}?{string.Join('&', query)}";
    }

    private async Task<CargoApiResult<T>> GetAsync<T>(
        string path,
        string failureTitle,
        string failureDetail,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                path,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new CargoApiResult<T>(
                    default,
                    await ReadFailureAsync(response, failureTitle, failureDetail, cancellationToken));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return new CargoApiResult<T>(value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
    }

    private Task<CargoApiResult<T>> PostAsync<T>(
        string path,
        object request,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendPostAsync<T>(
            path,
            JsonContent.Create(request, request.GetType()),
            failureTitle,
            cancellationToken);
    }

    private Task<CargoApiResult<T>> PostAsync<T>(
        string path,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        return SendPostAsync<T>(path, content: null, failureTitle, cancellationToken);
    }

    private async Task<CargoApiResult<T>> SendPostAsync<T>(
        string path,
        HttpContent? content,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(path, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new CargoApiResult<T>(
                    default,
                    await ReadFailureAsync(response, failureTitle, ReviewValuesDetail, cancellationToken));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return new CargoApiResult<T>(value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
    }

    private async Task<CargoApiResult<T>> SendManifestAsync<T>(string path, Stream stream, string fileName, string failureTitle, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        content.Add(file, "file", fileName);
        return await SendPostAsync<T>(path, content, failureTitle, cancellationToken);
    }

    private async Task<CargoApiResult<T>> PutAsync<T>(
    string path,
    object request,
    string failureTitle,
    CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PutAsync(
                path,
                JsonContent.Create(request, request.GetType()),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new CargoApiResult<T>(
                    default,
                    await ReadFailureAsync(response, failureTitle, ReviewValuesDetail, cancellationToken));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return new CargoApiResult<T>(value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
    }
    private static async Task<CargoApiFailure> ReadFailureAsync(
        HttpResponseMessage response,
        string failureTitle,
        string failureDetail,
        CancellationToken cancellationToken)
    {
        ApiProblemDetails? problem = null;

        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
        }
        catch (JsonException)
        {
            // Not every failure carries a problem details body; keep the page retryable.
        }

        return new CargoApiFailure(
            (int)response.StatusCode,
            problem?.Title ?? failureTitle,
            problem?.Detail ?? failureDetail,
            problem?.Errors ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    private static CargoApiFailure Unavailable()
    {
        return new CargoApiFailure(
            0,
            "HarborOps API is unavailable",
            "The cargo service cannot be reached right now.",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }
}
