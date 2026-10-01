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
        CancellationToken cancellationToken) =>
        GetContainersAsync(bookingId, CargoContainerFilter.None, pageNumber, cancellationToken);

    internal Task<CargoApiResult<CargoContainerList>> GetContainersAsync(
        Guid bookingId,
        CargoContainerFilter filter,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return GetAsync<CargoContainerList>(
            BuildContainersPath(bookingId, filter, pageNumber),
            "Unable to load the container manifest",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoManifestPrintView>> GetManifestPrintAsync(
        Guid bookingId,
        CargoContainerFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return GetAsync<CargoManifestPrintView>(
            BuildManifestOutputPath(bookingId, "print", filter),
            "Unable to prepare the printable manifest",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<HttpResponseMessage> DownloadManifestCsvAsync(
        Guid bookingId,
        CargoContainerFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return httpClient.GetAsync(
            BuildManifestOutputPath(bookingId, "export", filter),
            HttpCompletionOption.ResponseContentRead,
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

    internal Task<CargoApiResult<ReeferProfileView>> GetReeferProfileAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        return GetAsync<ReeferProfileView>(
            $"{ReeferPath(containerId)}/profile",
            "Unable to load the reefer monitoring profile",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<ReeferProfileRow>> SaveReeferProfileAsync(
        Guid containerId,
        SaveReeferProfileWebRequest request,
        CancellationToken cancellationToken)
    {
        return PutAsync<ReeferProfileRow>(
            $"{ReeferPath(containerId)}/profile",
            request,
            "Unable to save the reefer monitoring profile",
            cancellationToken);
    }

    internal Task<CargoApiResult<ReeferReadingPage>> GetReeferReadingsAsync(
        Guid containerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return GetAsync<ReeferReadingPage>(
            $"{ReeferPath(containerId)}/readings?pageNumber={pageNumber}&pageSize={pageSize}",
            "Unable to load the temperature log",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<RecordedReeferReading>> RecordReeferReadingAsync(
        Guid containerId,
        RecordReeferReadingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RecordedReeferReading>(
            $"{ReeferPath(containerId)}/readings",
            request,
            "Unable to record the temperature reading",
            cancellationToken);
    }

    internal Task<CargoApiResult<RecordedReeferReading>> CorrectReeferReadingAsync(
        Guid containerId,
        Guid readingId,
        CorrectReeferReadingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RecordedReeferReading>(
            $"{ReeferPath(containerId)}/readings/{readingId}/correct",
            request,
            "Unable to correct the temperature reading",
            cancellationToken);
    }

    internal Task<CargoApiResult<ReeferReadingRow>> VoidReeferReadingAsync(
        Guid containerId,
        Guid readingId,
        VoidReeferReadingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ReeferReadingRow>(
            $"{ReeferPath(containerId)}/readings/{readingId}/void",
            request,
            "Unable to void the temperature reading",
            cancellationToken);
    }

    internal Task<CargoApiResult<ReeferExcursionList>> GetReeferExcursionsAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        return GetAsync<ReeferExcursionList>(
            $"{ReeferPath(containerId)}/excursions",
            "Unable to load the temperature excursions",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<ReeferExcursionRow>> ResolveReeferExcursionAsync(
        Guid containerId,
        Guid excursionId,
        ResolveReeferExcursionWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ReeferExcursionRow>(
            $"{ReeferPath(containerId)}/excursions/{excursionId}/resolve",
            request,
            "Unable to resolve the temperature excursion",
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

    internal Task<CargoApiResult<CargoDiscrepancyPage>> GetDiscrepanciesAsync(
        Guid bookingId,
        CargoDiscrepancyFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber}",
            $"pageSize={filter.PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Insert(0, $"status={Uri.EscapeDataString(filter.Status)}");
        }

        if (filter.SourceExcursionId is { } sourceExcursionId)
        {
            query.Insert(0, $"sourceExcursionId={sourceExcursionId}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            query.Insert(0, $"type={Uri.EscapeDataString(filter.Type)}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Severity))
        {
            query.Insert(0, $"severity={Uri.EscapeDataString(filter.Severity)}");
        }

        return GetAsync<CargoDiscrepancyPage>(
            $"{DiscrepanciesPath(bookingId)}?{string.Join('&', query)}",
            "Unable to load the discrepancies",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoDiscrepancyView>> GetDiscrepancyAsync(
        Guid bookingId,
        Guid discrepancyId,
        CancellationToken cancellationToken)
    {
        return GetAsync<CargoDiscrepancyView>(
            $"{DiscrepanciesPath(bookingId)}/{discrepancyId}",
            "Unable to load the discrepancy",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoCompletionBlockersView>> GetCompletionBlockersAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<CargoCompletionBlockersView>(
            $"{DiscrepanciesPath(bookingId)}/completion-blockers",
            "Unable to load the completion blockers",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoDiscrepancyView>> RegisterDiscrepancyAsync(
        Guid bookingId,
        RegisterCargoDiscrepancyWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoDiscrepancyView>(
            DiscrepanciesPath(bookingId),
            request,
            "Unable to register the discrepancy",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoDiscrepancyView>> StartDiscrepancyInvestigationAsync(
        Guid bookingId,
        Guid discrepancyId,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoDiscrepancyView>(
            $"{DiscrepanciesPath(bookingId)}/{discrepancyId}/investigation",
            "Unable to start the investigation",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoDiscrepancyView>> AddDiscrepancyNoteAsync(
        Guid bookingId,
        Guid discrepancyId,
        AddCargoDiscrepancyNoteWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoDiscrepancyView>(
            $"{DiscrepanciesPath(bookingId)}/{discrepancyId}/notes",
            request,
            "Unable to add the note",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoDiscrepancyView>> ResolveDiscrepancyAsync(
        Guid bookingId,
        Guid discrepancyId,
        ResolveCargoDiscrepancyWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoDiscrepancyView>(
            $"{DiscrepanciesPath(bookingId)}/{discrepancyId}/resolution",
            request,
            "Unable to resolve the discrepancy",
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoDiscrepancyView>> VoidDiscrepancyAsync(
        Guid bookingId,
        Guid discrepancyId,
        VoidCargoDiscrepancyWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<CargoDiscrepancyView>(
            $"{DiscrepanciesPath(bookingId)}/{discrepancyId}/void",
            request,
            "Unable to void the discrepancy",
            cancellationToken);
    }

    internal Task<CargoApiResult<OpenedExcursionDiscrepancy>> OpenExcursionDiscrepancyAsync(
        Guid containerId,
        Guid excursionId,
        OpenExcursionDiscrepancyWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<OpenedExcursionDiscrepancy>(
            $"{ReeferPath(containerId)}/excursions/{excursionId}/discrepancy",
            request,
            "Unable to open a discrepancy for the excursion",
            cancellationToken);
    }

    private static string DiscrepanciesPath(Guid bookingId)
    {
        return $"{BasePath}/{bookingId}/discrepancies";
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

    private static string ReeferPath(Guid containerId)
    {
        return $"{ContainerPath(containerId)}/reefer";
    }

    private static string SafetyPath(Guid bookingId)
    {
        return $"{BasePath}/{bookingId}/safety-information";
    }
    private static string BuildContainersPath(Guid bookingId, CargoContainerFilter filter, int pageNumber)
    {
        var query = new List<string>(filter.QueryParts())
        {
            $"pageNumber={pageNumber}",
            "pageSize=15",
        };

        return $"{BasePath}/{bookingId}/containers?{string.Join('&', query)}";
    }

    private static string BuildManifestOutputPath(Guid bookingId, string output, CargoContainerFilter filter)
    {
        var query = filter.QueryParts();
        var path = $"{BasePath}/{bookingId}/containers/{output}";

        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
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

        if (filter.HasOpenDiscrepancies is { } hasOpenDiscrepancies)
        {
            query.Add($"hasOpenDiscrepancies={(hasOpenDiscrepancies ? "true" : "false")}");
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
        CargoApiProblemDetails? problem = null;

        try
        {
            problem = await response.Content.ReadFromJsonAsync<CargoApiProblemDetails>(cancellationToken);
        }
        catch (JsonException)
        {
            // Not every failure carries a problem details body; keep the page retryable.
        }

        return new CargoApiFailure(
            (int)response.StatusCode,
            problem?.Title ?? failureTitle,
            problem?.Detail ?? failureDetail,
            problem?.Errors ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            problem?.CompletionBlockers);
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
