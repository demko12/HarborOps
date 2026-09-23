using HarborOps.Modules.Notifications.Application;
using Compliance = HarborOps.Modules.Compliance.Contracts;
using CrewContracts = HarborOps.Modules.Crew.Contracts;
using Documents = HarborOps.Modules.Documents.Contracts;

namespace HarborOps.Api.Composition.Notifications;

public sealed class ExpiryAlertGenerator(
    Documents.IExpiryAlertCandidateQuery documents,
    CrewContracts.IExpiryAlertCandidateQuery crew,
    Compliance.IExpiryAlertCandidateQuery compliance,
    NotificationService notifications,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<ExpiryAlertOptions> options)
{
    public async Task GenerateAsync(CancellationToken cancellationToken)
    {
        var thresholds = options.Value.ThresholdDays.Where(x => x >= 0).Distinct().OrderDescending().ToArray();
        if (thresholds.Length == 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var to = today.AddDays(thresholds[0]);
        foreach (var item in await documents.GetCandidatesAsync(today, to, cancellationToken))
        {
            await Add("documents", item.SourceType, item.SourceId, item.VesselId, null, item.ExpiryDate, today, cancellationToken);
        }

        foreach (var item in await crew.GetCandidatesAsync(today, to, cancellationToken))
        {
            await Add("crew", item.SourceType, item.SourceId, null, item.CrewMemberId, item.ExpiryDate, today, cancellationToken);
        }

        foreach (var item in await compliance.GetCandidatesAsync(today, to, cancellationToken))
        {
            await Add("compliance", item.SourceType, item.SourceId, item.VesselId, null, item.ExpiryDate, today, cancellationToken);
        }
    }

    private async Task Add(
        string capability,
        string type,
        Guid id,
        Guid? vessel,
        Guid? crewMember,
        DateOnly expiry,
        DateOnly today,
        CancellationToken ct)
    {
        foreach (var threshold in options.Value.ThresholdDays.Where(x => x >= 0).Distinct())
            if (expiry == today.AddDays(threshold))
            {
                await notifications.GenerateAsync(capability, type, id, vessel, crewMember, expiry, threshold, ct);
            }
    }
}
