using HarborOps.Modules.Notifications.Contracts;
using HarborOps.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Notifications.Application;

public sealed class NotificationService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task GenerateAsync(string capability, string sourceType, Guid sourceId, Guid? vesselId, Guid? crewMemberId, DateOnly expiryDate, int thresholdDays, CancellationToken cancellationToken)
    {
        if (await dbContext.Set<OperationAlert>().AnyAsync(x => x.SourceId == sourceId && x.ExpiryDate == expiryDate && x.ThresholdDays == thresholdDays, cancellationToken)) return;
        var severity = thresholdDays switch { <= 7 => "critical", <= 30 => "warning", _ => "info" };
        await dbContext.AddAsync(new OperationAlert(capability, sourceType, sourceId, vesselId, crewMemberId, expiryDate, thresholdDays, severity, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
        try { await dbContext.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { }
    }
    public async Task<bool> GenerateOperationAlertAsync(string capability, string sourceType, Guid sourceId, DateOnly effectiveDate, string severity, CancellationToken cancellationToken)
    {
        if (await AlertExistsAsync(sourceId, cancellationToken)) return true;
        var entry = await dbContext.AddAsync(new OperationAlert(capability, sourceType, sourceId, null, null, effectiveDate, 0, severity, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { entry.State = EntityState.Detached; return await AlertExistsAsync(sourceId, cancellationToken); }
        return true;
    }
    public Task<bool> AlertExistsAsync(Guid sourceId, CancellationToken cancellationToken) => dbContext.Set<OperationAlert>().AsNoTracking().AnyAsync(x => x.SourceId == sourceId, cancellationToken);
    public async Task<IReadOnlySet<Guid>> FindAlertedSourcesAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (sourceIds.Count == 0) return new HashSet<Guid>();
        var alerted = await dbContext.Set<OperationAlert>().AsNoTracking().Where(x => sourceIds.Contains(x.SourceId)).Select(x => x.SourceId).Distinct().ToListAsync(cancellationToken);
        return alerted.ToHashSet();
    }
    public async Task<NotificationsPage> GetAsync(GetNotificationsRequest request, Guid userId, IReadOnlySet<string> capabilities, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.PageNumber ?? 1); var size = Math.Clamp(request.PageSize ?? 25, 1, 100);
        var alerts = dbContext.Set<OperationAlert>().AsNoTracking().Where(x => capabilities.Contains(x.Capability));
        var acknowledged = dbContext.Set<NotificationAcknowledgement>().AsNoTracking().Where(x => x.UserId == userId);
        var rows = await (
            from alert in alerts
            join acknowledgement in acknowledged on alert.Id equals acknowledgement.AlertId into acknowledgementJoin
            from acknowledgement in acknowledgementJoin.DefaultIfEmpty()
            select new
            {
                Alert = alert,
                IsAcknowledged = acknowledgement != null,
            })
            .OrderBy(x => x.IsAcknowledged)
            .ThenBy(x => x.Alert.ExpiryDate)
            .ThenBy(x => x.Alert.Id)
            .Select(x => new OperationAlertItem(
                x.Alert.Id,
                x.Alert.Capability,
                x.Alert.SourceType,
                x.Alert.SourceId,
                x.Alert.VesselId,
                x.Alert.CrewMemberId,
                x.Alert.ExpiryDate,
                x.Alert.ThresholdDays,
                x.Alert.Severity,
                x.Alert.GeneratedAtUtc,
                x.IsAcknowledged))
            .ToListAsync(cancellationToken);
        var filtered = request.Status?.ToLowerInvariant() switch { "unread" => rows.Where(x => !x.IsAcknowledged), "read" => rows.Where(x => x.IsAcknowledged), "all" or null or "" => rows, _ => throw new ArgumentException("Status must be unread, read, or all.", nameof(request)) };
        var list = filtered.ToList(); return new(list.Skip((page - 1) * size).Take(size).ToList(), page, size, list.Count, rows.Count(x => !x.IsAcknowledged));
    }
    public async Task<bool> AcknowledgeAsync(Guid alertId, Guid userId, IReadOnlySet<string> capabilities, CancellationToken cancellationToken)
    {
        var alert = await dbContext.Set<OperationAlert>().SingleOrDefaultAsync(x => x.Id == alertId, cancellationToken);
        if (alert is null || !capabilities.Contains(alert.Capability)) return false;
        if (!await dbContext.Set<NotificationAcknowledgement>().AnyAsync(x => x.AlertId == alertId && x.UserId == userId, cancellationToken))
        {
            await dbContext.AddAsync(new NotificationAcknowledgement(alertId, userId, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
            try { await dbContext.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { }
        }
        return true;
    }
    public async Task AcknowledgeAllAsync(Guid userId, IReadOnlySet<string> capabilities, CancellationToken cancellationToken)
    {
        var ids = await dbContext.Set<OperationAlert>().AsNoTracking().Where(x => capabilities.Contains(x.Capability)).Select(x => x.Id).ToListAsync(cancellationToken);
        var acknowledged = await dbContext.Set<NotificationAcknowledgement>().Where(x => x.UserId == userId && ids.Contains(x.AlertId)).Select(x => x.AlertId).ToListAsync(cancellationToken);
        foreach (var id in ids.Except(acknowledged)) await dbContext.AddAsync(new NotificationAcknowledgement(id, userId, timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
        try { await dbContext.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { }
    }
}
