using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class PortCallStatementPrint : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();

    private PortCallStatementModel? model;
    private TimeZoneInfo? portZone;
    private VoyagesApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedStopId;

    [Parameter]
    public Guid VoyageId { get; set; }

    [Parameter]
    public Guid StopId { get; set; }

    private string TimelineHref => $"/voyages/{VoyageId}";

    private string PageHeading =>
        model is { } statement
            ? $"Statement {statement.DocumentNumber}"
            : "Port-call statement";

    private int ColumnCount => portZone is null ? 5 : 6;

    private string TimeZoneLabel =>
        portZone is not null && model?.PortTimeZone is { } zoneId
            ? zoneId
            : "UTC (port time zone not set)";

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (loadedStopId == StopId && !isLoading)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;
        loadedStopId = StopId;

        try
        {
            var result = await VoyagesApi.GetPortCallStatementPrintAsync(
                VoyageId,
                StopId,
                disposeCancellation.Token);

            if (result.IsSuccess)
            {
                model = result.Value;
                portZone = PortCallActivityPanel.ResolveZone(result.Value?.PortTimeZone);
            }
            else
            {
                model = null;
                portZone = null;
                failure = result.Failure;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoading = false;
        }
    }

    private static string FormatLocalRange(DateTime occurredAtUtc, DateTime? endedAtUtc, TimeZoneInfo zone)
    {
        return endedAtUtc is { } end
            ? $"{PortCallActivityPanel.FormatPortLocal(occurredAtUtc, zone)} – {PortCallActivityPanel.FormatPortLocal(end, zone)}"
            : PortCallActivityPanel.FormatPortLocal(occurredAtUtc, zone);
    }

    private string FormatTime(DateTime? utc)
    {
        return portZone is null || utc is null
            ? PortCallActivityPanel.FormatUtc(utc)
            : $"{PortCallActivityPanel.FormatPortLocal(utc, portZone)} · {PortCallActivityPanel.FormatUtc(utc)}";
    }
}
