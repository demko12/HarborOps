using System.Globalization;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoManifestPrint : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();

    private CargoManifestPrintView? model;
    private CargoApiFailure? failure;
    private bool isLoading = true;
    private string? loadedKey;

    [Parameter]
    public Guid BookingId { get; set; }

    [SupplyParameterFromQuery(Name = "search")]
    public string? Search { get; set; }

    [SupplyParameterFromQuery(Name = "status")]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = "containerType")]
    public string? ContainerType { get; set; }

    [SupplyParameterFromQuery(Name = "portOfLoadingCode")]
    public string? PortOfLoadingCode { get; set; }

    [SupplyParameterFromQuery(Name = "portOfDischargeCode")]
    public string? PortOfDischargeCode { get; set; }

    [SupplyParameterFromQuery(Name = CargoBookingDetails.BoardQueryKey)]
    public string? BoardQuery { get; set; }

    [SupplyParameterFromQuery(Name = "returnPage")]
    public int? ReturnPage { get; set; }

    private CargoContainerFilter Filter =>
        CargoContainerFilter.Create(Search, Status, ContainerType, PortOfLoadingCode, PortOfDischargeCode);

    private string BookingHref =>
        "/" + CargoBookingDetails.BuildManifestAddress(BookingId, BoardQuery, Filter, ReturnPage is > 1 ? ReturnPage.Value : 1);

    private string PageHeading =>
        model is { } manifest ? $"Container manifest {manifest.BookingNumber}" : "Container manifest";

    private bool IsLimitFailure => failure is { IsValidation: true } && failure.Errors.ContainsKey("limit");

    private string FailureMessage =>
        failure is null
            ? string.Empty
            : failure.Errors.Count > 0
                ? string.Join(" ", failure.Errors.SelectMany(error => error.Value))
                : failure.Message;

    private static IEnumerable<(string Label, string Value)> AppliedFilters(CargoManifestPrintFilters filters)
    {
        if (filters.Search is { } search) yield return ("Number contains", search);
        if (filters.Status is { } status) yield return ("Status", status);
        if (filters.ContainerType is { } containerType) yield return ("Type", containerType);
        if (filters.PortOfLoadingCode is { } portOfLoading) yield return ("POL", portOfLoading);
        if (filters.PortOfDischargeCode is { } portOfDischarge) yield return ("POD", portOfDischarge);
    }

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Containers(int value) => $"{Count(value)} {(value == 1 ? "container" : "containers")}";

    private static string MatchingSummary(CargoManifestPrintView manifest)
    {
        return manifest.IsFiltered
            ? $"{Containers(manifest.MatchingCount)} of {Count(manifest.BookingContainerCount)} on this booking"
            : $"All {Containers(manifest.BookingContainerCount)} on this booking";
    }

    private static string KnownWeightSummary(CargoManifestPrintView manifest)
    {
        return manifest.KnownWeightCount == 0
            ? "No known weights"
            : $"{CargoBookingFormat.WeightKg(manifest.KnownWeightKg)} kg across {Containers(manifest.KnownWeightCount)}";
    }

    private static string UnknownWeightSummary(CargoManifestPrintView manifest)
    {
        return manifest.UnknownWeightCount == 0
            ? "None"
            : $"{Containers(manifest.UnknownWeightCount)} with unknown weight, not included in the total";
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override Task OnParametersSetAsync()
    {
        var key = $"{BookingId}|{string.Join('&', Filter.QueryParts())}";

        if (key == loadedKey)
        {
            return Task.CompletedTask;
        }

        loadedKey = key;

        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;

        try
        {
            var result = await CargoApi.GetManifestPrintAsync(BookingId, Filter, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            model = result.IsSuccess ? result.Value : null;
            failure = result.Failure;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;
    }

    private async Task PrintAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("print", disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
    }
}
